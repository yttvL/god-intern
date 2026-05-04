# ASCII Shader Notes

## Update 2026-05-04

### BUGS

1. GameView亮度异常
GameView容易过曝，亮度被压在少数区间，ASCII灰度等级不够  
SceneView表现正常

2. GameView边缘破碎
Sobel 对 binary DoG mask 在边带两侧响应  
8x8 cell voting 时 horizontal / vertical 票数占优  
导致 diagonal 边被拆成横 + 竖

3. "/"方向问题
"/"方向在GameView中几乎检测不到  
被分解成 horizontal + vertical  
但 SceneView 正常

---

### TODO

[ ]亮度处理
saturate 改为 tone mapping（Reinhard）后再做 luminance

[ ]Sobel采样
增加 SobelStep：  
texel = _BlitTexture_TexelSize.xy * _SobelStep  
用于控制 Sobel 采样邻域大小

[ ]voting
用 sobel.z 获取边 magnitude  
改为 magnitude-weighted voting 
如果 diagonal 和 horizontal / vertical 票数接近  
可以给 diagonal 一点 bias，避免斜边被拆