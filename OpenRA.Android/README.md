# OpenRA.Android — Android 应用壳（Phase 1 骨架）

本目录是在现代 OpenRA（net10.0）主线上新增的 Android 应用工程，目标是把 OpenRA + 尤复之战（RA2:YR）搬上 Android 并实现原生触屏。

## 关键结论
- nuget 核实：`OpenRA-SDL2-CS 1.0.44` 支持 `net10.0-android` 且**自带 Android 原生 SDL2 库**。
- 因此**直接复用 `OpenRA.Platforms.Default`**（Sdl2PlatformWindow / Sdl2Input / Sdl2GraphicsContext + Embedded GLES 路径），不必从零写平台后端，也不必搬旧 fork 的 SDL2Droid。

## 已落地（Phase 1）
- `OpenRA.Android.csproj`：net10.0-android 应用工程，引用引擎 + 默认平台 + 内置 mod（ra/cnc/d2k）。
- `Properties/AndroidManifest.xml`：权限/SDK/GLES3 声明。
- `MainActivity.cs`：占位 Activity（Phase 2 托管 SDL2 表面 + 启动游戏循环）。
- `.github/workflows/android.yml`：setup-dotnet 10 + android workload + Android SDK → 出 .apk。
- `OpenRA.Game/Platform.cs`：PlatformType 加 Android，并用 `#if __ANDROID__` 检测。

## 待办 / 需 CI 验证
1. FreeType6、OpenAL-CS 是否带 android 原生库（无则音频走 DummySoundEngine 回退、字体需另解）。
2. SDL2 的 Android Java Activity 接线 + OpenRA 游戏循环入口（参考桌面 OpenRA.WindowsLauncher）。
3. 资源交付：把 `mods/` 内容与用户自备的 `.mix` 送到 Android 可访问位置（应用私有存储 / SAF）。
4. 触控输入翻译（MotionEvent → MouseInput/KeyInput，照 Sdl2Input.PumpInput 模板）。
5. YR mod（cookgreen/Yuris-Revenge）接入。

## 如何验证（本机无 SDK/NDK，必须 CI）
1. 在 GitHub fork OpenRA 到你的账号。
2. 把 `OpenRA.Android/`、`OpenRA.Game/Platform.cs` 改动、`.github/workflows/android.yml` 提交到 `android` 分支。
3. 在仓库 Actions 运行 `Android APK` 工作流，下载产物 .apk 侧载到手机。
4. CI 编译结果回传后，据报错迭代（主要是上述 1–2 项）。

## 法律
引擎与 YR mod 均为 GPLv3，本 Android 移植同样 GPLv3 并开源；APK 不打包 EA 的 `.mix` 资源。
