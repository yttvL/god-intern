# ASCII Shader Notes

## Update 2026-05-08

1. ASCII Preset Workflow
新增 RenderFeature preset 保存/恢复流程  
使用 ScriptableObject 保存 ASCII visual settings  
可直接在 RenderFeature Inspector 中：
- Save Render Feature -> Preset
- Apply Preset -> Render Feature

---

## Update 2026-05-07

### BUG FIX

1. GameView边缘破碎 和 "/"方向问题

问题现象：
SceneView 中 diagonal edge 检测正常
GameView 中"/"方向的 diagonal edge 完全丢失
edge 会被拆分成 horizontal + vertical
debug edge view 同样被拆分， 非voting问题

最终原因：
_ASCII_PingTex, _ASCII_DoGTex, _ASCII_SobelTex
默认直接继承 cameraTargetDescriptor.graphicsFormat

GameView 下 URP HDR 默认使用：
GraphicsFormat.B10G11R11_UFloatPack32

该格式为：
HDR, Unsigned Float, 不支持负数

但：
DoG difference, Sobel gradient, theta direction 本质上都包含负数。

结果：
所有负 theta 被硬截断为 0, absTheta 变为 0
direction classification 落入：
  if (absTheta >= 0.0 && absTheta < 0.05), 最终被错误归类为 horizontal edge

而 SceneView：
Editor 内部通常使用 Signed Float RT, 可以正常保留负数, 因此方向检测正常

修复方法：
GraphicsFormat signedFormat = GraphicsFormat.R16G16B16A16_SFloat;

---

## Update 2026-05-05

1. Luminance 对比度异常（Compute阶段）
LuminanceExtract pass 时明暗对比正常  
进入 Compute（读取 downscaled luminance）后整体对比度显著下降，接近全灰  
猜测：问题不在 URP tonemapping 而在 downscaled luminance 本身已被压平  

2. Frame Debugger 中 Compute 表现异常
ComputeGlyphs 在 Frame Debugger 中出现两次（灰度图 + 最终结果）

3. Luminance / Pack 修正
Luminance tone mapping 后表现正常  
问题主要来自 PackColorLuminance 后使用 downscaled alpha 作为 compute luminance 输入  
当前 compute 阶段改为直接从 downscaled RGB 重新计算 luminance  
结果：亮度层级恢复正常，暂时不再使用 `.w` 作为 glyph luminance 输入

---

## Update 2026-05-04

### BUGS

[x] GameView亮度异常
GameView容易过曝，亮度被压在少数区间，ASCII灰度等级不够  
SceneView表现正常

[x] GameView边缘破碎
Sobel 对 binary DoG mask 在边带两侧响应  
8x8 cell voting 时 horizontal / vertical 票数占优  
导致 diagonal 边被拆成横 + 竖

[x] "/"方向问题
"/"方向在GameView中几乎检测不到  
被分解成 horizontal + vertical  
但 SceneView 正常

---

### TODO

[x] 亮度处理
saturate 改为 tone mapping（Reinhard）后再做 luminance

[ ] Sobel采样
增加 SobelStep：  
texel = _BlitTexture_TexelSize.xy * _SobelStep  
用于控制 Sobel 采样邻域大小

[ ] voting
用 sobel.z 获取边 magnitude  
改为 magnitude-weighted voting 
如果 diagonal 和 horizontal / vertical 票数接近  
可以给 diagonal 一点 bias，避免斜边被拆

---

### NEW FEATURES

1. Fill Color
默认白色用于整体 ASCII tint
可与 downscaled color 叠乘

2. Separate Edge Color
可选开关
开启后 edge 使用独立颜色