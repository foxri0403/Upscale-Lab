import { useEffect, useRef, useState, type RefObject } from "react";
import { rigTargets } from "./character-rig";
import { showcaseRig } from "./showcase-character";

export const portraitImage = "/images/live2d-portrait.jpg";

const vertexSource = `
attribute vec2 position;
varying vec2 uv;
void main() {
  uv = vec2(position.x + 1.0, 1.0 - position.y) * 0.5;
  gl_Position = vec4(position, 0.0, 1.0);
}`;

// Feather the displacement around the neck, hair and irises so the portrait
// responds in the same regions as the PNG rig without hard cutout edges.
const fragmentSource = `
precision mediump float;
varying vec2 uv;
uniform sampler2D portrait;
uniform vec2 head;
uniform vec2 gaze;
uniform float hair;

float influence(vec2 point, vec2 center, vec2 radius) {
  return 1.0 - smoothstep(0.2, 1.0, length((point - center) / radius));
}
vec2 turn(vec2 point, vec2 pivot, float angle) {
  vec2 d = (point - pivot) * vec2(1.0, 1.5);
  float c = cos(angle), s = sin(angle);
  return (mat2(c, s, -s, c) * d - d) / vec2(1.0, 1.5);
}
void main() {
  float face = influence(uv, vec2(0.5, 0.40), vec2(0.48, 0.38));
  vec2 point = uv + (turn(uv, vec2(0.5, 0.67), -head.x) - vec2(0.0, head.y)) * face;
  float fringe = influence(point, vec2(0.5, 0.25), vec2(0.41, 0.19));
  float eyes = max(influence(point, vec2(0.422, 0.429), vec2(0.038, 0.021)),
                   influence(point, vec2(0.591, 0.428), vec2(0.038, 0.021)));
  point += turn(point, vec2(0.5, 0.38), -hair) * fringe - gaze * eyes;
  gl_FragColor = texture2D(portrait, point);
}`;

function compile(gl: WebGLRenderingContext, type: number, source: string) {
  const shader = gl.createShader(type);
  if (!shader) return null;
  gl.shaderSource(shader, source);
  gl.compileShader(shader);
  if (gl.getShaderParameter(shader, gl.COMPILE_STATUS)) return shader;
  gl.deleteShader(shader);
  return null;
}

export function PortraitMotion({ input, active }: { input: RefObject<{ x: number; y: number }>; active: boolean }) {
  const image = useRef<HTMLImageElement>(null);
  const canvas = useRef<HTMLCanvasElement>(null);
  const [loaded, setLoaded] = useState(false);
  useEffect(() => {
    const picture = image.current, surface = canvas.current;
    if (!active || !loaded || !picture || !surface) return;
    const gl = surface.getContext("webgl", { alpha: false, antialias: false });
    if (!gl) return;
    const vertex = compile(gl, gl.VERTEX_SHADER, vertexSource);
    const fragment = compile(gl, gl.FRAGMENT_SHADER, fragmentSource);
    const program = gl.createProgram(), buffer = gl.createBuffer(), texture = gl.createTexture();
    let frame = 0;
    const dispose = () => {
      cancelAnimationFrame(frame);
      surface.style.visibility = "hidden";
      gl.deleteShader(vertex);
      gl.deleteShader(fragment);
      gl.deleteProgram(program);
      gl.deleteBuffer(buffer);
      gl.deleteTexture(texture);
    };
    if (!vertex || !fragment || !program || !buffer || !texture) return dispose;
    gl.attachShader(program, vertex);
    gl.attachShader(program, fragment);
    gl.linkProgram(program);
    if (!gl.getProgramParameter(program, gl.LINK_STATUS)) return dispose;
    gl.useProgram(program);
    gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 1, -1, -1, 1, 1, 1]), gl.STATIC_DRAW);
    const position = gl.getAttribLocation(program, "position");
    gl.enableVertexAttribArray(position);
    gl.vertexAttribPointer(position, 2, gl.FLOAT, false, 0, 0);
    gl.bindTexture(gl.TEXTURE_2D, texture);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGB, gl.RGB, gl.UNSIGNED_BYTE, picture);
    gl.viewport(0, 0, surface.width, surface.height);
    const head = gl.getUniformLocation(program, "head");
    const gaze = gl.getUniformLocation(program, "gaze");
    const hair = gl.getUniformLocation(program, "hair");
    function draw(time: number) {
      if (!gl || !surface || gl.isContextLost()) return;
      const { x, y } = input.current;
      const target = rigTargets(x, y, x, y, time / 1000, showcaseRig);
      gl.uniform2f(head, target.head * 0.8, target.shiftY);
      gl.uniform2f(gaze, target.eye * 0.65, target.eyeY * 0.6);
      gl.uniform1f(hair, target.front * 0.85);
      gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
      surface.style.visibility = "visible";
      frame = requestAnimationFrame(draw);
    }
    frame = requestAnimationFrame(draw);
    const lost = () => { surface.style.visibility = "hidden"; };
    surface.addEventListener("webglcontextlost", lost);
    return () => { surface.removeEventListener("webglcontextlost", lost); dispose(); };
  }, [active, input, loaded]);

  return <div className="demo-portrait">
    <img ref={image} src={portraitImage} alt="마우스와 기울기에 반응하는 남성 인물 사진" width={1024} height={1536}
      loading="lazy" onLoad={() => setLoaded(true)} />
    <canvas ref={canvas} width={640} height={960} style={{ visibility: "hidden" }} aria-hidden="true" />
  </div>;
}
