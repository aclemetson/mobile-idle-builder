using System;
using System.IO;
using NUnit.Framework;

// No namespace: NUnit runs a namespace-less SetUpFixture once around every test in the assembly.

/// <summary>
/// Points <see cref="MobileIdleBuilder.LocalSaveService.SaveDirectory"/> at a temp folder for the
/// whole run, so no test in this assembly can overwrite or delete the developer's real save.
/// The PlayModeTests assembly has the same fixture (PlayModeTestsSaveDirectory).
/// </summary>
[SetUpFixture]
public class EditModeTestsSaveDirectory
{
    string _dir;

    [OneTimeSetUp]
    public void Redirect()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mib-tests-" + Guid.NewGuid().ToString("N"));
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
