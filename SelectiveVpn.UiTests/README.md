# Isolated WPF regression checks

Run on Windows with the .NET 10 SDK:

```powershell
dotnet run --project SelectiveVpn.UiTests/SelectiveVpn.UiTests.csproj -c Release -p:MSBuildEnableWorkloadResolver=false
```

The executable uses a deterministic STA dispatcher, the actual `MainViewModel`, a fake Mihomo controller, temporary profile files, in-memory settings and intercepted dialogs. It never constructs the production `App` or `MainWindow`, reads real user settings, starts Mihomo, changes Windows startup entries, or launches elevation. The popup checks use an off-screen generic WPF window and the real app styles/source markup.

Each check prints `PASS` or `FAIL`; a failing check returns a nonzero exit code. The runner has no external test-framework dependency. Core engine/parser tests remain in `SelectiveVpn.Tests` and run with `dotnet test`.
