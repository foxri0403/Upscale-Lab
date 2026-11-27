# 집 PC GPU 워커

Windows의 NVIDIA GPU에서 업스케일(Real-ESRGAN), 사진 배경 분리(BiRefNet + LaMa), Live2D 캐릭터 파츠 분리(See-through)를 실행하는 개발용 워커다. GPU 워커는 `127.0.0.1:18189`에 바인딩하며, 기존 API가 사용하는 `LocalGpu` 작업 제출·조회·취소 규격과 S3 서명 URL을 받는다. AWS 자격은 API에서 관리한다. 집 PC의 Python 환경, 모델 파일, 워커 키는 Git에서 제외된다.

## 준비

저장소 루트에서 64비트 Python 3.11과 PowerShell을 사용한다. 환경은 RTX 50 시리즈에 필요한 CUDA 12.8 PyTorch로 구성한다. See-through 모델 준비에는 약 5.7GB의 모델 다운로드가 추가된다.

```powershell
.\gpu-worker-home\home-gpu.ps1 -Mode Install -WithLayers -WithCharacter
.\gpu-worker-home\prepare-models.ps1 -WithLayers -WithCharacter
```

업스케일만 실행하려면 `-WithLayers`와 `-WithCharacter`를 빼고 `-Mode Install`을 실행한다. 모델 준비는 사용할 분리 기능의 플래그만 지정한다. 고정 버전과 체크섬은 스크립트에 있다. See-through는 공개 NF4 모델과 1024px 정사각형 캔버스를 사용한다.

## 실행

첫 번째 PowerShell에서 워커를 시작한다. 설치한 기능의 플래그를 사용한다.

```powershell
.\gpu-worker-home\home-gpu.ps1 -Mode Worker -WithLayers -WithCharacter
```

두 번째 PowerShell에서 준비 상태를 확인하고, 기존 개발용 PostgreSQL·JWT·Cognito·AWS S3 설정이 로드된 상태로 API를 실행한다. SDK가 PATH에 없으면 `-DotNet`에 `dotnet.exe` 경로를 전달한다.

```powershell
.\gpu-worker-home\home-gpu.ps1 -Mode Check
.\gpu-worker-home\home-gpu.ps1 -Mode Api
```

API는 `http://127.0.0.1:5080`에서 실행하며 워커가 제공하는 작업을 활성화한다. 웹 개발 서버는 해당 로컬 API를 사용하도록 [웹 개발 가이드](../web/README.md)에 따라 시작한다. 취소와 시간 초과 뒤에는 워커가 다음 추론 단계에서 작업을 종료한다. 진행 중 작업 상태는 워커 메모리에 있으므로 워커를 재시작한 경우 중단된 작업을 다시 시작한다.

## 학교 GPU로 복귀

집에서 처리 중인 작업을 마친 뒤 `-Mode Api`와 워커를 종료한다. 학교에서 기존 API 실행 방식과 SSH 터널 또는 HTTPS 설정을 사용한다. 학교의 `Processing:Provider`, `LocalGpu:ApiBaseUrl`, `LocalGpu:ApiKey`, `SeeThrough` 연결 설정은 원래 보안 설정에서 다시 읽힌다. `-Mode Api`가 적용한 집 설정의 범위는 해당 API 실행이다.
