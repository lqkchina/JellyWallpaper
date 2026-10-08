//======================================================================
// Jelly.fx —— 果冻形变像素着色器 (Shader Model 3.0, PS 3.0)
//
// 作用：把壁纸纹理按鼠标按压点做径向"向内凹陷"的平滑形变。
//   - 形变使用高斯型衰减（无振荡），按压区域平滑下凹，松开后平滑回弹，
//     绝不产生"一圈圈向外扩散的圆环波纹"。
//   - 只作用于壁纸图层的采样坐标，不修改任何窗口层级或鼠标事件。
//
// 寄存器约定（与 JellyEffect.cs 中的 PixelShaderConstantCallback 一一对应）：
//   s0 : 壁纸（元素自身渲染内容）纹理采样器
//   c0 : MouseX   鼠标归一化 X (0..1)
//   c1 : MouseY   鼠标归一化 Y (0..1)
//   c2 : Depth    按压深度（按下 0->1，松开 1->0，回弹允许轻微过冲）
//   c3 : Strength 形变强度
//   c4 : Falloff  高斯衰减系数 sigma^2（越小越集中）
//   c5 : Shade    按压阴影强度（增强立体凹陷感）
//======================================================================
sampler2D input : register(s0);

float MouseX : register(c0);
float MouseY : register(c1);
float Depth  : register(c2);
float Strength : register(c3);
float Falloff  : register(c4);
float Shade    : register(c5);

float4 main(float2 uv : TEXCOORD) : COLOR
{
    // 鼠标位置（归一化坐标）
    float2 mouse = float2(MouseX, MouseY);

    // 当前像素到按压点的方向向量
    float2 delta = uv - mouse;
    float  r2 = dot(delta, delta);

    // 高斯型衰减：越靠近按压点影响越大，向外平滑衰减，无波纹
    float falloff = exp(-r2 / max(Falloff, 0.0001));

    // 形变深度（按压时为正值，回弹过冲时可为轻微负值）
    float depth = Depth;

    // 采样点向内凹陷：把周围像素向按压点方向偏移，形成"压下去"的果冻凹面
    float2 sampleUv = uv - delta * (Strength * depth * falloff);

    float4 col = tex2D(input, sampleUv);

    // 按压阴影：按压中心略微变暗，增强立体感（可按需关闭）
    float shadow = 1.0 - Shade * abs(depth) * falloff;
    col.rgb *= saturate(shadow);

    return col;
}
