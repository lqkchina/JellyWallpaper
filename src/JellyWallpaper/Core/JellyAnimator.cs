namespace JellyWallpaper.Core;

/// <summary>
/// 果冻形变动画器：阻尼弹簧模型，驱动"按压深度"从 0->1（按下）和 1->0（松开）。
/// 欠阻尼设置会产生轻微过冲，形成 Q 弹回弹手感。
/// 参数由设置面板控制：回弹速度(刚度)、按压衰减系数(阻尼)。
/// </summary>
internal sealed class JellyAnimator
{
    private double _velocity;
    private double _target;   // 目标深度：按下=1，松开=0
    private double _lastTime; // 上次步进时间（秒）
    private bool _running;

    /// <summary>当前深度（可为轻微负值表示过冲回弹）。</summary>
    public double Depth { get; private set; }

    /// <summary>按压目标（1）还是松开目标（0）。</summary>
    public double Target => _target;

    /// <summary>是否正在动画中。</summary>
    public bool IsRunning => _running;

    /// <summary>设置弹簧参数。</summary>
    public void SetParams(double stiffness, double damping)
    {
        // 刚度不能为 0
        Stiffness = Math.Max(stiffness, 1.0);
        Damping = Math.Max(damping, 0.0);
    }

    public double Stiffness { get; private set; } = 70;
    public double Damping { get; private set; } = 12;

    /// <summary>按下：目标深度设为 1。</summary>
    public void Press()
    {
        _target = 1.0;
        _running = true;
        _lastTime = 0;
    }

    /// <summary>松开：目标深度设为 0，进入回弹。</summary>
    public void Release()
    {
        _target = 0.0;
        _running = true;
        _lastTime = 0;
    }

    /// <summary>
    /// 步进一帧。返回是否已稳定（可停止渲染循环）。
    /// </summary>
    public bool Step(double nowSeconds)
    {
        if (!_running) return true;

        double dt = _lastTime > 0 ? nowSeconds - _lastTime : 0.0;
        _lastTime = nowSeconds;
        dt = Math.Clamp(dt, 0.0, 0.05); // 防止掉帧导致爆炸

        // 阻尼弹簧：a = -k*(pos-target) - c*vel
        double accel = -Stiffness * (Depth - _target) - Damping * _velocity;
        _velocity += accel * dt;
        Depth += _velocity * dt;

        // 安全限幅，避免异常参数导致数值发散
        if (Math.Abs(Depth) > 3.0)
        {
            Depth = Math.Sign(Depth) * 3.0;
            _velocity = 0;
        }

        // 收敛判定
        if (Math.Abs(Depth - _target) < 0.0005 && Math.Abs(_velocity) < 0.0005)
        {
            Depth = _target;
            _velocity = 0;
            _running = false;
            return true;
        }
        return false;
    }
}
