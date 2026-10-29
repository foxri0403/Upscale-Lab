using ImageMagick;
using UpscaleLab.Application.Images;
using UpscaleLab.Application.Wallpapers;

namespace UpscaleLab.Infrastructure.Wallpapers;

internal sealed class LayerPresentation
{
    public WallpaperRenderSettings Settings { get; }
    public int Width { get; }
    public int Height { get; }
    private readonly int cropWidth, cropHeight, originalWidth, originalHeight;
    private readonly int cropX, cropY;
    private readonly double factor;
    public bool ChangesPixels => Settings.Filter != "original" || Settings.Crop != "original" || Settings.Scale != 1;
    public LayerPresentation(WallpaperRenderSettings settings, int width, int height)
    {
        ImageEditState.Create(new(settings.Strength, settings.Filter, settings.Crop, settings.Scale, settings.Foreground, CropRegion: settings.CropRegion));
        Settings=settings;originalWidth=width;originalHeight=height;
        var selected=ImageCropGeometry.Resolve(width,height,settings.Crop,settings.CropRegion);
        cropX=selected.X;cropY=selected.Y;cropWidth=selected.Width;cropHeight=selected.Height;
        factor=!ChangesPixels ? 1 : Math.Min(settings.Scale,4096.0/Math.Max(cropWidth,cropHeight));
        Width=Math.Max(1,(int)Math.Round(cropWidth*factor));Height=Math.Max(1,(int)Math.Round(cropHeight*factor));
    }
    public WallpaperFile Transform(WallpaperFile file) => file with { X=file.X*factor,Y=file.Y*factor,
        MovementX=file.Role=="Background"?0:Width*.035*Settings.Strength/100,
        MovementY=file.Role=="Background"?0:Height*.035*Settings.Strength/100 };
    public byte[] Render(byte[] bytes)
    {
        using var image=new MagickImage(bytes);
        if(image.Width!=originalWidth || image.Height!=originalHeight)throw new InvalidDataException("레이어 크기가 일치하지 않습니다.");
        image.Crop(new MagickGeometry(cropX,cropY,(uint)cropWidth,(uint)cropHeight));image.ResetPage();
        image.Resize((uint)Width,(uint)Height);
        if(Settings.Filter!="original")
        {
            image.Depth=8;var pixels=image.ToByteArray(MagickFormat.Rgba);
            for(var i=0;i<pixels.Length;i+=4)
            {
                double r=pixels[i]/255.0,g=pixels[i+1]/255.0,b=pixels[i+2]/255.0;
                void Saturate(double value){var gray=.2126*r+.7152*g+.0722*b;r=gray+(r-gray)*value;g=gray+(g-gray)*value;b=gray+(b-gray)*value;}
                switch(Settings.Filter){
                    case "mono": r=g=b=.2126*r+.7152*g+.0722*b;break;
                    case "vivid": Saturate(1.4);r=(r-.5)*1.08+.5;g=(g-.5)*1.08+.5;b=(b-.5)*1.08+.5;break;
                    case "soft": r=((r-.5)*.88+.5)*1.1;g=((g-.5)*.88+.5)*1.1;b=((b-.5)*.88+.5)*1.1;break;
                    case "warm": var sr=.393*r+.769*g+.189*b;var sg=.349*r+.686*g+.168*b;var sb=.272*r+.534*g+.131*b;
                        r=r*.78+sr*.22;g=g*.78+sg*.22;b=b*.78+sb*.22;Saturate(1.15);break;
                }
                pixels[i]=(byte)Math.Clamp(Math.Round(r*255),0,255);pixels[i+1]=(byte)Math.Clamp(Math.Round(g*255),0,255);pixels[i+2]=(byte)Math.Clamp(Math.Round(b*255),0,255);
            }
            image.Read(pixels,new MagickReadSettings{Format=MagickFormat.Rgba,Width=(uint)Width,Height=(uint)Height,Depth=8});
        }
        return image.ToByteArray(MagickFormat.Png);
    }
}
