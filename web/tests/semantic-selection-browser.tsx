// Real SlimSAM inference on a bundled public sample. No API or RunPod requests.
import { useEffect, useRef, useState } from "react";
import { createRoot } from "react-dom/client";
import { SubjectSeparationCanvas, SubjectSeparationControls, useSubjectSeparation } from "../src/SubjectSeparation";
import { SmartSelectionClient } from "../src/smart-selection-client";
import { loadImage, type Picture } from "../src/model";
import { unspecifiedTag } from "../src/image-tags";

const width=3072,height=2048;
function board(){const c=document.createElement('canvas');c.width=width;c.height=height;return c;}
function Fixture(){
  const [picture,setPicture]=useState<Picture>(),[diagnostics,setDiagnostics]=useState(''),[result,setResult]=useState('');
  const baseline=useRef<Uint8Array | undefined>(undefined),lastGuide=useRef<{alpha:Uint8Array;width:number;height:number} | undefined>(undefined);
  const client=useRef<SmartSelectionClient | undefined>(undefined);
  const editor=useSubjectSeparation(picture,true,!picture,{
    additive:true,
    refine:async(input,progress)=>{
      lastGuide.current={alpha:input.guide.slice(),width:input.width,height:input.height};
      client.current ||=new SmartSelectionClient(()=>{});
      const value=await client.current.refine(input,progress);
      let painted=0,excluded=0,selected=0;
      for(let i=0;i<value.alpha.length;i++)if(lastGuide.current.alpha[i]>200){painted++;excluded+=value.alpha[i]===0?1:0;selected+=value.alpha[i]>127?1:0;}
      setDiagnostics(JSON.stringify({paintedPixels:painted,backgroundPixelsExcluded:excluded,subjectPixels:selected}));
      return value;
    },
  });
  useEffect(()=>{
    let alive=true;
    void(async()=>{
      const image=await loadImage('/images/fox.png'),source=board(),c=source.getContext('2d')!;
      c.drawImage(image,0,0,width,height);const rgb=c.getImageData(0,0,width,height),alpha=new Uint8Array(width*height);
      for(let y=0;y<height;y++)for(let x=0;x<width;x++){
        const i=y*width+x;alpha[i]=Math.hypot(x-1500,y-1000)<160?128+(x+y)%128:0;rgb.data[i*4+3]=alpha[i];
      }
      const subject=board();subject.getContext('2d')!.putImageData(rgb,0,0);baseline.current=alpha;
      if(alive)setPicture({id:'real-foreground-fixture',name:'브러쉬 안의 피사체만 추가',src:source.toDataURL(),background:source.toDataURL(),
        foregrounds:[subject.toDataURL()],subjectLayerId:'automatic-baseline',width,height,genre:unspecifiedTag,created:'2026-10-31T00:00:00Z'});
    })();
    return()=>{alive=false;client.current?.dispose();};
  },[]);
  async function inspect(){
    const url=URL.createObjectURL(await editor.exportMask());
    try{
      const image=await loadImage(url),c=board().getContext('2d')!;c.drawImage(image,0,0);
      const data=c.getImageData(0,0,width,height).data;let added=0,decreased=0,outsideHintChanges=0,baselineChanges=0;
      const guide=lastGuide.current;
      for(let y=0;y<height;y++)for(let x=0;x<width;x++){
        const i=y*width+x,previous=baseline.current![i],value=data[i*4];
        if(previous===value)continue;
        added+=value>previous?1:0;decreased+=value<previous?1:0;baselineChanges+=previous>0?1:0;
        if(guide){
          const gx=Math.floor(x/width*guide.width),gy=Math.floor(y/height*guide.height);let covered=false;
          for(let dy=-1;dy<=1;dy++)for(let dx=-1;dx<=1;dx++){
            const px=gx+dx,py=gy+dy;if(px>=0&&px<guide.width&&py>=0&&py<guide.height&&guide.alpha[py*guide.width+px])covered=true;
          }
          outsideHintChanges+=covered?0:1;
        }
      }
      setResult(JSON.stringify({width:image.naturalWidth,height:image.naturalHeight,added,decreased,outsideHintChanges,baselineChanges},null,2));
    }finally{URL.revokeObjectURL(url);}
  }
  return <main style={{fontFamily:'sans-serif',padding:20}}>
    <h1>브러쉬 범위 안의 피사체 인식</h1><p>실제 SlimSAM을 사용합니다. 원본은 3072 × 2048이며 RunPod 작업은 제출하지 않습니다.</p>
    <div style={{display:'flex',gap:24}}><div style={{position:'relative',width:720,height:480,flexShrink:0}}>
      <SubjectSeparationCanvas editor={editor} disabled={!picture} filter="none" />
    </div><div style={{width:330}}><SubjectSeparationControls editor={editor} disabled={false} hasResult={!!picture} apply={()=>void inspect()} selectTool={()=>editor.setView('overlay')} />
      <button disabled={!editor.ready||editor.tracing} onClick={()=>void inspect()}>원본 마스크 픽셀 검사</button>
      <p role="status">{editor.tracing?editor.smartStatus:editor.error||'준비 완료'}</p>
      <pre aria-label="인식 결과">{diagnostics}</pre><pre aria-label="픽셀 검사">{result}</pre>
    </div></div>
  </main>;
}
createRoot(document.getElementById('root')!).render(<Fixture />);
