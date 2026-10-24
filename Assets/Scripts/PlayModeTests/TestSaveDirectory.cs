using System;
using System.IO;
using NUnit.Framework;

// No namespace: NUnit runs a namespace-less SetUpFixture once around every test in the assembly.

/// <summary>
/// Points <see cref="MobileIdleBuilder.LocalSaveService.SaveDirectory"/> at a temp folder for the
/// whole run. Without it, SaveWipeTests overwrote and then deleted the developer's real save.json
/// and sidecar files, so every test run silently reset Play Mode progress to a fresh install.
/// </summary>
[SetUpFixture]
public class PlayModeTestsSaveDirectory
{
    string _dir;

    [OneTimeSetUp]
    public void Redirect()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mib-playmodetests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        MobileIdleBuilder.LocalSaveService.SaveDirectoryOverride = _dir;
    }

    [OneTimeTearDown]
    public void Restore()
    {
        MobileIdleBuilder.LocalSaveService.SaveDirectoryOverride = null;
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }
}
