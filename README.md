# RC Plaza — RC 遥控车物理模拟游戏

按 `RC模拟游戏设计.md` 实现的 Unity 2022.3 完整工程：两辆真实参数还原的遥控车
（短卡 SCT 型 / 大脚 MT 型）、自研 Pacejka 轮胎物理、raycast 悬挂、三段式无刷电机
扭矩曲线、锂电电压骤降与低压保护、三层程序化音频、住宅小区广场砖地场景，
性能锚点 100 Hz 物理 / 60 FPS。

**工程零二进制资产、零 .meta 文件：** 模型为原语拼装、纹理与音频全部程序化生成、
UI 纯代码、场景运行时构建——仓库只含文本文件，导入即可运行。

---

## 一、快速开始

1. **安装 Unity 2022.3 LTS**（推荐 2022.3.20f1，任意 2022.3.x 均可）：
   Unity Hub → Installs → 安装 Windows Build Support 的 2022.3 版本。
   无需任何额外模块（不依赖 Input System、URP、TextMeshPro）。
2. **打开工程**：Unity Hub → Open → Add project from disk → 选择本目录 `D:\RCPlaza`。
3. **首次导入**：等待 1–2 分钟（自动生成 Library 与全部 .meta）。
4. **运行**：打开 `Assets/RCPlaza/Scenes/RCPlaza_Minimal.unity` → 按 Play。
   > 双保险：即使不打开任何场景（例如编辑器里的 New Scene），按 Play 同样完整运行——
   > 入口是 `[RuntimeInitializeOnLoadMethod]` 引导，与场景内容无关。

## 二、操作

| 按键 | 功能 |
|---|---|
| `W` / `↑` | 前进（油门） |
| `S` / `↓` | 前进时刹车；低速持续按住 → 倒车（30% 功率，真实 ESC 逻辑） |
| `A` `D` / `←` `→` | 转向（Ackermann，含舵机滞后 0.08s/60°） |
| `Space` | 手刹 |
| `Tab` | 切换车型：短卡 SCT（2.64kg，红壳） ⇄ 大脚 MT（4.85kg，蓝壳） |
| `R` | 复位到出生点 |
| `C` | 视角切换：追尾 ⇄ 车顶 FPV |
| `F1` | 帮助面板 |
| `F2` | 物理调试面板：每轮 Fz/Fx/Fy/λ/α/ω、转速扭矩、电压电流 SOC、车身侧倾、FPS |
| `F3` | 滑行测试（自动执行，结果横幅显示） |
| `F4` | 0→40/70 km/h 加速计时（静止时按下） |

场景玩法：4.5 cm 混凝土路沿——**短卡（离地 3.2 cm）需助跑或斜切才能翻上，
大脚（离地 5.5 cm）可直接骑上路沿**；广场四周外还有草地（低抓地）、碎石带与
灯柱/长椅/花坛/树木等可碰撞装饰物。

## 三、设计文档验收对照（§7）

| 文档验收项 | 期望 | 工程内核对方式 |
|---|---|---|
| ① 定速收油滑行距离 | 3–6 m（解析值 v²/2Crr·g ≈ 4.1 m） | **F3**：自动加速到 1 m/s 收油，横幅报实测距离 |
| ② 0→40 km/h 加速 | 2.5–3.5 s（MT 为 0→70 km/h） | **F4**：静止全油门自动计时 |
| ③ 25 km/h 定圆侧倾 | 3–5° | **F2** 面板实时看"车身侧倾"；建议半舵跑大半径定圆（满舵最小半径仅约 0.66 m，25 km/h 下 a≈5g 必然翻车） |
| ④ 电池电压骤降与低压保护 | SOC/电压衰减，LVC 限功/切断 | 右上角 HUD 实时电压/SOC；`PlazaConfig.DischargeTimeScale`（默认 2.0）控制演示倍速；<3.4V/芯限功 50%、<3.2V/芯切断 |

## 四、代码结构

```
Assets/RCPlaza/
├─ Scenes/RCPlaza_Minimal.unity      极简空场景（仅 RenderSettings 等，不引用任何脚本）
└─ Scripts/                          （17 个文件，命名空间 RCPlaza.*）
   ├─ Core/    PlazaConfig(全局/性能/场景常数) · SurfaceData(μ/Crr/Pacejka 系数表)
   │           CarSpec(两车全参数) · PacejkaTyre(魔术公式+摩擦椭圆) · RcInput(键位+舵机滞后)
   ├─ Sim/     WheelUnit(raycast 悬挂+Pacejka+ω 积分) · MotorAndBattery(三段扭矩+电压 sag+LVC)
   │           RcCarController(FixedUpdate 数据流:输入→悬挂→轮胎力→ω→ARB→风阻→休眠)
   ├─ World/   PlazaBuilder(场景构建) · TextureFactory(程序纹理) · MaterialFactory · SurfaceRegion
   ├─ Cam/     ChaseCamera(追尾+FPV,FOV 随速)
   ├─ UI/      HudController(UGUI 纯代码,中文字体,含调试面板)
   ├─ Audio/   NoiseFactory(白噪/棕噪/谐波合成) · RcAudioEngine(电机+齿轮+胎噪交叉淡化+风噪)
   └─ Root/    GameBootstrap(RuntimeInitializeOnLoadMethod 引导 + GameHost 主循环)
```

物理数据流（100 Hz）：输入 → 电调软启动 → 电池/电机（V=V_ocv−I·R，三段扭矩曲线）
→ 四轮 raycast 悬挂（弹簧+隐式阻尼）→ 接触点速度分解出 λ/α → Pacejka+摩擦椭圆
→ `AddForceAtPosition`（力作用于接触点 → 自然重量转移/俯仰/侧倾）→ 车轮 ω 积分
→ 防倾杆 + 空气阻力 → 静止 1s 无油门自动休眠。

## 五、对设计文档的两处公开偏差

1. **悬架参数修正**：文档 §3.3 的 k=28000–32000 N/m 会使 2.64 kg 车身静态压缩仅
   0.21 mm（悬挂视觉冻结），25 km/h 定圆侧倾仅 0.04°，与文档自身的
   "TargetPosition 0.5"与"侧倾 3–5°"验收目标矛盾。修正为按"静载压缩 ≈ 50% 行程"
   反推：SCT k≈370 N/m、c≈25 N·s/m；MT k≈530、c≈40。
   文档原值保留在 `CarSpec.cs` 注释中备查。
2. **电机扭矩曲线**：文档曲线在 0 转速处扭矩为 0（车辆无法起步，是不动点），
   故保留 0.30 堵转扭矩兜底；并在 95%→100% 转速段将残余扭矩衰减到 0，
   使极速由"风阻 × 扭矩衰减"自然平衡产生（纯文档曲线没有真实极速）。
   峰值扭矩亦按 §7② 验收反向推导：SCT 0.085 N·m（0→40≈2.9 s，极速≈55 km/h）、
   MT 0.35 N·m（0→70≈2.7 s，极速≈92 km/h）。

车型命名采用风格化描述（"短卡 SCT 型 / 大脚 MT 型"），参数注释标注了参考原型，
不复制品牌标识与精确外观（文档 §8）。

## 六、性能账目

- `Time.fixedDeltaTime = 0.01`（100 Hz 物理）、`Application.targetFrameRate = 60`；
- Rigidbody：CCD + Interpolate、`maxAngularVelocity=20`、`solverIterations=8`；
- 场景 90 个静态物体 → 两次 `StaticBatchingUtility.Combine` 合批，共享 ≈15 份材质；
- 每辆车仅 4 条悬挂射线（`Physics.DefaultRaycastLayers` 排除汽车自身的
  Ignore Raycast 层）、1 个刚体、1 个碰撞体；F2 面板左上角实时 FPS 可核对。

## 七、构建 Windows exe

菜单 File → Build Settings → 勾选 `Scenes/RCPlaza_Minimal` → 平台 Windows →
Player Settings 中分辨率自定 → Build。运行后按 F1 查看操作。

## 八、常见问题

- **首次打开卡的久 / 太多 .meta 变更**：正常——这是 Unity 为全部脚本/场景生成的元数据。
- **Play 后汽车不动**：检查 Console；本工程所有输入均为 `Input.GetKey`，
  与 InputManager 轴配置 / Input System 包无关。
- **有任何编译报错**：把 Console 中红字报错完整发回即可快速修复（工程无二进制
  资产，任何文件都可直接改）。
- **想调手感**：入口是 `Scripts/Core/CarSpec.cs`（车参数）与 `SurfaceData.cs`
  （路面 μ/滚阻）、`PlazaConfig.cs`（电池倍速/场景尺寸）。