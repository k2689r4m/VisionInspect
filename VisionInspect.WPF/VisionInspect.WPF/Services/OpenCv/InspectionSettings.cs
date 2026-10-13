namespace VisionInspect.WPF.Services.OpenCv;

public sealed class InspectionSettings
{
    // 공통 설정
    public double MinDefectArea { get; set; } = 50;
    public int Threshold { get; set; } = 30;

    // Difference
    public bool EnableAlignment { get; set; } = true;

    // Difference 고도화 설정
    public bool EnableBrightnessCompensation { get; set; } = true;
    public int DifferenceBlurSize { get; set; } = 5;
    public int DifferenceMorphSize { get; set; } = 3;
    public int AlignmentMinMatches { get; set; } = 12;
    public double AlignmentMinInlierRatio { get; set; } = 0.50;
    public int AlignmentBorderMargin { get; set; } = 5;
    public double AlignmentMaxScaleChange { get; set; } = 0.15;
    public double AlignmentMaxPerspectiveShift { get; set; } = 0.02;

    // Shape
    public double MinContourArea { get; set; } = 50;
    public double MaxContourArea { get; set; } = 100000;

    // Template
    public double TemplateThreshold { get; set; } = 0.8;

    // Dimension
    public double MinWidth { get; set; } = 0;
    public double MaxWidth { get; set; } = double.MaxValue;
    public double MinHeight { get; set; } = 0;
    public double MaxHeight { get; set; } = double.MaxValue;

    // Surface
    public int KernelSize { get; set; } = 9;

    // Color (HSV)
    public int HueMin { get; set; } = 0;
    public int HueMax { get; set; } = 179;
    public int SaturationMin { get; set; } = 0;
    public int SaturationMax { get; set; } = 255;
    public int ValueMin { get; set; } = 0;
    public int ValueMax { get; set; } = 255;

    // Feature
    public int MaxFeatures { get; set; } = 500;
    public double MatchRatio { get; set; } = 0.75;

    // Feature Matching
    public int MinGoodMatches { get; set; } = 12;

    // RANSAC으로 계산한 대응점 중 정상 대응점의 최소 비율
    public double MinInlierRatio { get; set; } = 0.5;

    // RANSAC 재투영 오차 허용값 (pixel)
    public double RansacThreshold { get; set; } = 3.0;

    // 매칭 결과를 화면에 표시할 최대 개수
    public int MaxDrawMatches { get; set; } = 50;

    // Shape Inspection
    public double MinAspectRatio { get; set; } = 0.0;
    public double MaxAspectRatio { get; set; } = double.MaxValue;

    // 검사할 객체의 최소 면적
    public double MinObjectArea { get; set; } = 100;

    // 이진화 색상 반전
    public bool InvertBinary { get; set; } = false;

    // Template Matching
    public bool EnablePositionInspection { get; set; } = false;

    // 정상 템플릿이 있어야 할 기준 좌표
    public int ExpectedX { get; set; } = 0;
    public int ExpectedY { get; set; } = 0;

    // 허용 위치 오차 (pixel)
    public double PositionTolerance { get; set; } = 10;

    // Dimension Measurement
    public double MinAngle { get; set; } = -90;
    public double MaxAngle { get; set; } = 90;

    // 픽셀을 실제 길이로 환산할 때 사용
    // 보정 전에는 1.0으로 유지 (단위: px)
    public double MmPerPixel { get; set; } = 1.0;

    // Surface Inspection
    public bool DetectBrightDefects { get; set; } = true;
    public bool DetectDarkDefects { get; set; } = true;

    // 주변 대비 밝기 차이 임계값 (0~255)
    public int SurfaceThreshold { get; set; } = 25;

    // 형태학 연산 커널 크기 (홀수, 3 이상)
    public int SurfaceKernelSize { get; set; } = 15;

    // 검사 대상에서 제외할 작은 결함 후보 면적
    public double MinSurfaceDefectArea { get; set; } = 20;

    // Color Inspection
    public bool ColorIsForbidden { get; set; } = true;

    // 최소 검출 영역 면적 (pixel²)
    public double MinColorArea { get; set; } = 30;

    // Required Color 모드에서 필요한 최소 픽셀 비율 (%)
    public double MinRequiredColorRatio { get; set; } = 5.0;

    // Shape Inspection - 고도화
    public bool ShapeUseOtsu { get; set; } = true;
    public int ShapeBlurSize { get; set; } = 5;
    public int ShapeMorphSize { get; set; } = 3;
    // 0이면 객체 개수 검사를 수행하지 않음
    public int ShapeExpectedObjectCount { get; set; } = 0;
    public bool ShapeEnableCircularity { get; set; } = false;
    public double ShapeMinCircularity { get; set; } = 0.0;
    public double ShapeMaxCircularity { get; set; } = 1.0;
    public bool ShapeExcludeBorderObjects { get; set; } = false;
    public int ShapeBorderMargin { get; set; } = 0;
    // Template Inspection - 고도화
    public bool TemplateEnableRotation { get; set; } = false;
    public double TemplateMaxRotation { get; set; } = 10.0;
    public double TemplateRotationStep { get; set; } = 2.0;
    public bool TemplateEnableMultiMatch { get; set; } = false;
    public int TemplateExpectedCount { get; set; } = 1;
    public int TemplateMaxDetections { get; set; } = 50;
    public double TemplateNmsIoU { get; set; } = 0.3;

}
