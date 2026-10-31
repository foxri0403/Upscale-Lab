import { createRoot } from "react-dom/client";
import { Workspace } from "../src/Workspace";
import { sampleLibrary } from "../src/model";
import "../src/styles.css";
import "../src/responsive.css";

const library = sampleLibrary();
library.devices = [
  { id: "pc-fixture", name: "작업용 PC", platform: "Windows", online: false,
    displays: [
      { id: "one", name: "모니터 1", resolution: "1920 × 1080", connected: true, current: "lake", isPrimary: true },
      { id: "two", name: "보조 모니터", resolution: "2560 × 1440", connected: false, current: "lake", isPrimary: false },
    ] },
  { id: "phone-fixture", name: "휴대폰", platform: "Android", online: false,
    displays: [
      { id: "lock", target: "Lock", name: "잠금 화면", resolution: "1080 × 2400", connected: true, current: "lake" },
      { id: "home", target: "Home", name: "홈 화면", resolution: "1080 × 2400", connected: true, current: "lake" },
    ] },
];
localStorage.setItem("upscale.demo.library.v1.device-layout-fixture", JSON.stringify(library));
createRoot(document.getElementById("root")!).render(<Workspace session={{ id: "device-layout-fixture", name: "배치 검사", email: "fixture@upscalelab.local" }} route="/app/devices" logout={() => {}} />);
