#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Fails the build early with a clear error if broken Animator transitions are present.
public class PreBuildChecks : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        int issues = AnimatorValidator.ValidateAllControllers(out int total);
        if (issues > 0)
        {
            throw new BuildFailedException($"Pre-build check failed: found {issues} broken Animator transitions across {total} controllers. Open 'Tools/Animators/Validate Controllers (Report Broken)' for details.");
        }
    }
}
#endif
