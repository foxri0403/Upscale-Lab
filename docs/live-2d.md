# Live 2D와 레이어 조합

웹의 배경 분리와 Live 2D 캐릭터 파츠는 서로 다른 클라우드 처리 작업이다. 이미지를 업로드한 뒤 기존 작업실에서 사용한다. Android와 Windows는 웹에서 만든 결과물을 적용하는 연결 앱이다.

## 사진 배경과 피사체

배경 분리 탭은 ProcessingOperation.Layers로 BiRefNet_HR-matting + LaMa를 요청한다. 고정 배경과 투명 피사체를 만들고 포인터/기울기에 따라 시차를 미리본다. [수동 선택 보완](subject-selection.md)은 같은 원본 좌표에서 동작한다.

기기에 전달하는 불변 manifest는 레이어 순서·변환·움직임 입력·ON/OFF를 포함한다. Android는 자이로 센서, Windows는 마우스를 사용한다. 연결 앱의 계정 인증이 유효해야 동작하고 Android 최초 활성화는 시스템 승인이 필요하다. [연결 앱](companion-apps.md)에 빌드·인증·실기기 제한을 정리했다.

## 캐릭터 파츠와 웹 움직임

작업실의 Live 2D 탭에서 캐릭터 파츠 분리를 시작한다. CharacterLayers는 RunPodPretrainedSeeThroughProcessor의 전용 See-through RunPod endpoint를 사용한다. SeeThrough:Enabled, Provider=RunPod, endpoint ID와 API key가 준비된 경우에만 처리 가능으로 노출한다. 서버 로컬 Python/GPU 실행 경로는 사용하지 않는다.

결과 ZIP과 manifest를 검증하고 최대 64개의 PNG 레이어를 S3에 보관한다. 원격 작업 ID·입력 hash·제출 시각을 저장해 재시작 시 기존 작업을 조회한다. 진행률은 원격 단계이며 추론 완료율을 추정하지 않는다. [클라우드 처리 계약](cloud-processing.md)을 따른다.

웹은 저장된 layerOrder대로 파츠를 합성한다. 얼굴·눈·눈썹·머리카락 등 인식한 파츠에 회전/이동을 적용하고 몸통·의상·팔·장신구 등은 고정한다. 좌우·상하 테스트, 얼굴 회전 중심과 흔들림을 미리볼 수 있다. rig 설정은 기존 SavedEditJson에 저장한다. Cubism mesh/자동 리깅 모델과는 다른 PNG 파츠 변환 기능이며 범용 분할 정확도를 보장하지 않는다.

캐릭터 전용 레이어에 배경이 없으면 현재 기기용 Native Live 2D 자산을 만들지 않고 합성한 정지 PNG로 저장·적용한다. 웹의 rig를 Windows/Android의 움직이는 캐릭터 렌더러에 전달하는 단계는 아직 연결되지 않았다. 일반 배경+피사체 시차 기능과 구분해야 한다.

## 수동 레이어 순서

레이어가 2개 이상이면 수동 레이어 조합을 사용할 수 있다. 현재 순서를 미리보고 빈 캔버스에서 맨 아래 파츠부터 추가하거나 위·아래 이동/제거/되돌리기를 할 수 있다. 모든 파츠를 한 번씩 포함해야 저장 가능하고 Background 레이어는 가장 아래에 둔다.

PUT /api/projects/{projectId}/layers/order에 아래→위 순서의 layerIds를 보낸다. 소유권·공개 잠금·진행 중 작업·누락·중복·타인 레이어를 서버에서 검사한다. 고유 순서 인덱스 충돌을 피하도록 트랜잭션에서 임시 음수 순서를 거친 뒤 0..N-1로 저장한다. PNG 바이너리와 S3 key는 바꾸지 않으며 새 migration이 필요하지 않다.

AI 자동 순서는 최초 레이어 파일명의 순서로 복구하고 구형 이름에서는 현재 순서를 사용한다. 저장한 순서는 작업실·서버 조회·새 배경화면 manifest에 반영된다. 기존에 고정한 불변 결과물을 소급 변경하지 않는다.

## 검증

백엔드 CharacterLayerPipeline/CharacterRigEdit/WallpaperMotionDelivery 테스트와 웹 character-rig/layer-stack/parallax-motion 테스트를 실행한다. 연결 앱 센서·렌더러·삭제 테스트도 유지한다. 실제 원격 모델, Windows 다중 모니터, Android 홈/잠금 승인·재부팅·계정 전환은 실기기 검증이 필요하다.
