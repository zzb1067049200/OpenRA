using Android.App;
using Android.Content.PM;
using Android.OS;

namespace OpenRA.Android
{
    // Phase 1 占位 Activity：先让 .apk 能编译/安装。
    // 下一步（Phase 2）在此托管 SDL2 表面（SDL2-CS 的 Android Activity）并启动 OpenRA 游戏循环。
    [Activity(
        Label = "OpenRA",
        MainLauncher = true,
        Theme = "@android:style/Theme.NoTitleBar.Fullscreen",
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.KeyboardHidden | ConfigChanges.ScreenSize)]
    public class MainActivity : Activity
    {
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // TODO(Phase 2): 用 SDL2-CS 的 Android Activity 承载渲染表面，
            // 并调用 OpenRA.Game 的游戏入口（与桌面 OpenRA.WindowsLauncher 相同的启动路径）。
            // 当前仅验证 Android 工具链可编译/打包。
        }
    }
}
