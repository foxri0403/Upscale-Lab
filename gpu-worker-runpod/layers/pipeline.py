"""One CUDA inference per uploaded photo, returning two full-canvas PNG layers."""
import io
import pathlib

import numpy as np
from PIL import Image, ImageOps, ImageFilter

MAX_PIXELS = 64_000_000
MAX_BYTES = 256 * 1024 * 1024
Image.MAX_IMAGE_PIXELS = MAX_PIXELS


def read_image(content, width, height):
    with Image.open(io.BytesIO(content)) as source:
        if source.width * source.height > MAX_PIXELS or max(source.size) > 32768:
            raise ValueError("Input canvas exceeds the image limit.")
        image = ImageOps.exif_transpose(source).convert("RGB")
    if image.size != (width, height):
        raise ValueError("Input canvas does not match the request.")
    return image


def background_mask(alpha):
    # Include soft hair edges; dilation prevents the old silhouette remaining in the plate.
    mask = (np.asarray(alpha) > 2).astype(np.uint8)
    fraction = np.mean(np.asarray(alpha) > 127)
    if not .001 < fraction < .98:
        raise ValueError("No separable foreground/background was found.")
    radius = max(3, round(max(alpha.size) * .004))
    return np.asarray(Image.fromarray(mask).filter(ImageFilter.MaxFilter(radius * 2 + 1)))


def read_subject_mask(content, width, height):
    with Image.open(io.BytesIO(content)) as source:
        if source.format != "PNG" or source.size != (width, height) or width * height > MAX_PIXELS:
            raise ValueError("Subject mask must be a full-canvas PNG.")
        if source.convert("RGBA").getchannel("A").getextrema() != (255, 255):
            raise ValueError("Subject mask must be opaque grayscale.")
        alpha = source.convert("L")
    background_mask(alpha)  # Apply the same foreground/background limits as automatic separation.
    return alpha


def generator_options(config_path):
    from omegaconf import OmegaConf
    # Official big-lama YAML uses ${generator...} references. Resolve this subtree
    # without resolving training-only environment variables in the rest of the file.
    config = OmegaConf.load(config_path)
    return OmegaConf.to_container(config.generator, resolve=True)


class LayerPipeline:
    def __init__(self, root="/app/models"):
        import torch
        from transformers import AutoModelForImageSegmentation
        from saicinpainting.training.modules import make_generator

        if not torch.cuda.is_available():
            raise RuntimeError("Cloud CUDA GPU is required; CPU fallback is disabled.")
        self.torch = torch
        path = pathlib.Path(root)
        # trust_remote_code is restricted to the reviewed snapshot baked into the image.
        self.segmenter = AutoModelForImageSegmentation.from_pretrained(
            str(path / "birefnet"), trust_remote_code=True, local_files_only=True).eval().cuda().half()
        self.inpainter = make_generator({}, **generator_options(path / "big-lama/config.yaml"))
        # The official Lightning checkpoint can contain configuration objects. It is
        # checksum-verified while building the container and never supplied by a user.
        state = torch.load(path / "big-lama/models/best.ckpt", map_location="cpu", weights_only=False)["state_dict"]
        generator = {k.removeprefix("generator."): v for k, v in state.items() if k.startswith("generator.")}
        self.inpainter.load_state_dict(generator, strict=True)
        self.inpainter.eval().cuda()  # FFC/FFT stays FP32; only BiRefNet uses FP16.

    def split(self, image, alpha=None):
        torch = self.torch
        from torchvision import transforms
        transform = transforms.Compose([transforms.Resize((2048, 2048)), transforms.ToTensor(),
            transforms.Normalize([.485, .456, .406], [.229, .224, .225])])
        with torch.inference_mode():
            if alpha is None:
                prediction = self.segmenter(transform(image).unsqueeze(0).cuda().half())[-1].sigmoid()
                alpha = Image.fromarray((prediction[0, 0].float().cpu().numpy() * 255).round().astype(np.uint8))
                alpha = alpha.resize(image.size, Image.Resampling.LANCZOS)
            holes = background_mask(alpha)
            # Use the global scene, not independent segmentation tiles. Bound LaMa's VRAM
            # while keeping all original unmasked pixels at their original resolution.
            factor = min(1., 2048 / max(image.size))
            size = (max(8, round(image.width * factor)), max(8, round(image.height * factor)))
            rgb = np.asarray(image.resize(size, Image.Resampling.LANCZOS)).astype(np.float32) / 255
            mask = np.asarray(Image.fromarray(holes).resize(size, Image.Resampling.NEAREST)
                              .filter(ImageFilter.MaxFilter(3))).astype(np.float32)
            h, w = mask.shape
            rgb = np.pad(rgb, ((0, (-h) % 8), (0, (-w) % 8), (0, 0)), mode="symmetric")
            mask = np.pad(mask, ((0, (-h) % 8), (0, (-w) % 8)), mode="symmetric")
            pixels = torch.from_numpy(rgb.transpose(2, 0, 1)).unsqueeze(0).cuda()
            hole_tensor = torch.from_numpy(mask).unsqueeze(0).unsqueeze(0).cuda()
            output = self.inpainter(torch.cat([pixels * (1 - hole_tensor), hole_tensor], dim=1))
            plate = output[0, :, :h, :w].permute(1, 2, 0).clamp(0, 1).cpu().numpy()
        plate = Image.fromarray((plate * 255).round().astype(np.uint8)).resize(image.size, Image.Resampling.LANCZOS)
        background = Image.composite(plate, image, Image.fromarray(holes * 255))
        foreground = image.copy().convert("RGBA")
        foreground.putalpha(alpha)  # Preserve soft alpha; do not threshold the cutout.
        return background, foreground


def png_bytes(image):
    output = io.BytesIO()
    image.save(output, format="PNG")
    value = output.getvalue()
    if len(value) > MAX_BYTES:
        raise ValueError("Output file exceeds the file limit.")
    return value
