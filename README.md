# VisionInspect

C# WPF 기반 산업용 머신비전 검사 및 AI 이상 탐지 모델 비교 프로젝트입니다. OpenCvSharp를 활용한 7종 영상처리 알고리즘과 AI 모델 비교·성능 분석 화면을 제공합니다.

## 기술 스택

- **Desktop:** C#, WPF, XAML, MVVM, CommunityToolkit.Mvvm
- **Computer Vision:** OpenCvSharp / OpenCV
- **AI 이상 탐지 비교:** PatchCore, PaDiM, EfficientAD
- **평가 데이터:** MVTec AD (화면에 표시된 테스트 기준)

## 주요 화면 및 구현 로직

### 01. AI 모델 비교

![AI 모델 비교](screenshots/01.png)

PatchCore, PaDiM, EfficientAD를 동일한 검사 이미지에 적용해 이상 점수와 임계값으로 OK/NG를 판정하고, 결함 영역·추론 시간·결함 개수를 나란히 비교합니다. 화면에서 Original / Heatmap / Overlay / Defects 보기 방식을 전환할 수 있습니다.

### 02. AI 성능 벤치마크

![AI 성능 벤치마크](screenshots/02.png)

MVTec AD 데이터셋을 대상으로 모델별 Image AUROC, Image F1, Pixel AUROC, Pixel F1 및 평가 시간을 집계해 차트와 테이블로 비교합니다. 표시된 수치는 화면의 평가 실행 결과이며, 전체 데이터셋에 대한 보편적인 성능을 의미하지 않습니다.

### 03. OpenCV 통합 검사 화면

![OpenCV 통합 검사 화면](screenshots/03.png)

WPF와 MVVM으로 검사 설정·이미지 표시·결과 지표를 분리했습니다. 검사 모드별 설정을 받아 OpenCvSharp 알고리즘을 실행하고, 원본/주석 이미지 및 전처리 결과(Gray, Binary, Edge, Contour 등)를 확인할 수 있습니다.

### 04. Difference — 이미지 차이 검사

![Difference — 이미지 차이 검사](screenshots/04.png)

기준 이미지와 검사 이미지의 위치 차이를 ORB 특징점과 RANSAC으로 보정한 뒤, 픽셀 차이(AbsDiff)를 임계값 처리합니다. 형태학적 연산과 윤곽선 분석으로 차이 영역을 추출하고 면적 기준에 따라 NG를 판정합니다.

### 05. Shape — 형상 검사

![Shape — 형상 검사](screenshots/05.png)

그레이스케일·Otsu 이진화·형태학적 연산으로 객체를 분리하고, 윤곽선에서 면적·외곽 크기·원형도 등 형상 특징을 계산합니다. 지정한 허용 범위를 벗어나는 객체를 표시합니다.

### 06. Template — 패턴 매칭

![Template — 패턴 매칭](screenshots/06.png)

기준 템플릿을 검사 이미지에서 정규화 상관계수 기반으로 검색합니다. 유사도 임계값을 통과한 후보에 NMS(중복 제거)를 적용해 동일 패턴의 여러 위치를 검출하고, 필요하면 예상 개수와 비교합니다.

### 07. Dimension — 치수 검사

![Dimension — 치수 검사](screenshots/07.png)

이진화와 윤곽선 검출로 물체를 분리하고 MinAreaRect로 회전 외접 사각형을 계산합니다. 너비·높이·회전 각도를 측정하여 설정한 허용 범위와 비교하며, 보정 계수가 있으면 픽셀 길이를 실제 단위로 환산할 수 있습니다.

### 08. Surface — 표면 결함 검사

![Surface — 표면 결함 검사](screenshots/08.png)

TopHat과 BlackHat으로 주변 표면보다 밝거나 어두운 국소 영역을 강조합니다. 임계값·형태학적 연산·최소 면적 필터로 후보를 정제해 스크래치나 찢어짐 등 표면 이상을 표시합니다.

### 09. Color — 색상 검사

![Color — 색상 검사](screenshots/09.png)

BGR 이미지를 HSV로 변환한 후 Hue·Saturation·Value 범위를 InRange로 마스킹합니다. 검출 픽셀 비율과 영역을 계산하고 금지 색상 또는 필수 색상 조건에 따라 판정합니다. 어두운 얼룩은 Value 상한을 낮춰 검출할 수 있습니다.

### 10. Feature — 특징점 매칭

![Feature — 특징점 매칭](screenshots/10.png)

기준 이미지와 검사 이미지에서 ORB 특징점·기술자를 추출하고 BFMatcher 및 Lowe Ratio Test로 대응점을 선별합니다. RANSAC Homography와 Inlier 비율로 매칭 신뢰도를 판단하고, 인식된 대상의 투영 외곽선과 대응점을 시각화합니다. 개별 부품 결함을 직접 검출하는 기능은 아닙니다.

## 검사 처리 흐름

`이미지 입력 → 알고리즘/파라미터 선택 → 전처리 → 특징 추출·결함 후보 검출 → 규격/임계값 판정 → 결과 시각화 및 지표 표시`

기준 이미지는 Difference, Template, Feature 검사에서 필요하며, Shape·Dimension·Surface·Color 검사는 설정값을 기준으로 단일 이미지 검사가 가능합니다.

> **참고:** 본 프로젝트는 알고리즘 구현 및 기능 검증을 위한 포트폴리오입니다. 검사 결과는 입력 이미지와 파라미터에 따라 달라지며, 실제 생산 환경에 적용하려면 촬영 조건 표준화, 보정 및 추가 검증이 필요합니다.
