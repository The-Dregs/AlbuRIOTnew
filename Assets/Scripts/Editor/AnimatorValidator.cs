#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// tools -> animators -> validate controllers (report broken transitions)
// scans all AnimatorController assets and reports transitions that have no destination
// (and are not Exit) which commonly cause build-time NullReference in UnityEditor.Graphs.Edge.WakeUp.
public static class AnimatorValidator
{
    // returns the number of controllers scanned and the number of issues found
    public static int ValidateAllControllers(out int totalControllers)
    {
        totalControllers = 0;
        int issues = 0;
        var guids = AssetDatabase.FindAssets("t:AnimatorController");
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (ctrl == null) continue;
            totalControllers++;
            issues += ValidateController(ctrl, path);
        }
        return issues;
    }

    [MenuItem("Tools/Animators/Sanitize Controllers (Remove Orphaned Transitions)")]
    public static void SanitizeAll()
    {
        var guids = AssetDatabase.FindAssets("t:AnimatorController");
        int removed = 0;
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (ctrl == null) continue;
            removed += SanitizeController(ctrl, path);
        }
        if (removed > 0)
        {
            AssetDatabase.SaveAssets();
            Debug.LogWarning($"[AnimatorValidator] Sanitizer removed {removed} orphaned transition(s) across all controllers.");
        }
        else
        {
            Debug.Log($"[AnimatorValidator] Sanitizer: no orphaned transitions found.");
        }
    }

    private static int SanitizeController(AnimatorController ctrl, string path)
    {
        // build sets of all states and state machines within this controller
        var allStates = new HashSet<AnimatorState>();
        var allSms = new HashSet<AnimatorStateMachine>();
        foreach (var layer in ctrl.layers)
        {
            Collect(layer.stateMachine, allStates, allSms);
        }

        int removed = 0;
        foreach (var layer in ctrl.layers)
        {
            removed += SanitizeStateMachine(layer.stateMachine, path, allStates, allSms);
        }
        if (removed > 0)
        {
            EditorUtility.SetDirty(ctrl);
        }
        return removed;
    }

    private static void Collect(AnimatorStateMachine sm, HashSet<AnimatorState> states, HashSet<AnimatorStateMachine> sms)
    {
        if (sm == null) return;
        sms.Add(sm);
        foreach (var cs in sm.states)
        {
            if (cs.state != null) states.Add(cs.state);
        }
        foreach (var child in sm.stateMachines)
        {
            if (child.stateMachine != null) Collect(child.stateMachine, states, sms);
        }
    }

    private static int SanitizeStateMachine(AnimatorStateMachine sm, string context, HashSet<AnimatorState> allStates, HashSet<AnimatorStateMachine> allSms)
    {
        int removed = 0;
        // states' transitions
        foreach (var cs in sm.states)
        {
            var st = cs.state;
            if (st == null) continue;
            var list = new List<AnimatorStateTransition>(st.transitions);
            foreach (var t in list)
            {
                if (t == null) continue;
                bool bad = false;
                if (!t.isExit && t.destinationState == null && t.destinationStateMachine == null) bad = true;
                if (t.destinationState != null && !allStates.Contains(t.destinationState)) bad = true;
                if (t.destinationStateMachine != null && !allSms.Contains(t.destinationStateMachine)) bad = true;
                if (bad)
                {
                    st.RemoveTransition(t);
                    removed++;
                    Debug.LogWarning($"[AnimatorValidator] Removed orphaned transition in {context} > State '{st.name}'");
                }
            }
        }
        // any state transitions
        var anyList = new List<AnimatorStateTransition>(sm.anyStateTransitions);
        foreach (var t in anyList)
        {
            if (t == null) continue;
            bool bad = false;
            if (!t.isExit && t.destinationState == null && t.destinationStateMachine == null) bad = true;
            if (t.destinationState != null && !allStates.Contains(t.destinationState)) bad = true;
            if (t.destinationStateMachine != null && !allSms.Contains(t.destinationStateMachine)) bad = true;
            if (bad)
            {
                sm.RemoveAnyStateTransition(t);
                removed++;
                Debug.LogWarning($"[AnimatorValidator] Removed orphaned AnyState transition in {context}");
            }
        }
        // entry transitions
        var entryList = new List<AnimatorTransition>(sm.entryTransitions);
        foreach (var t in entryList)
        {
            if (t == null) continue;
            bool bad = false;
            if (!t.isExit && t.destinationState == null && t.destinationStateMachine == null) bad = true;
            if (t.destinationState != null && !allStates.Contains(t.destinationState)) bad = true;
            if (t.destinationStateMachine != null && !allSms.Contains(t.destinationStateMachine)) bad = true;
            if (bad)
            {
                sm.RemoveEntryTransition(t);
                removed++;
                Debug.LogWarning($"[AnimatorValidator] Removed orphaned Entry transition in {context}");
            }
        }
        // recurse
        foreach (var child in sm.stateMachines)
        {
            removed += SanitizeStateMachine(child.stateMachine, $"{context} > SubSM '{child.stateMachine.name}'", allStates, allSms);
        }
        return removed;
    }
    [MenuItem("Tools/Animators/Validate Controllers (Report Broken)")]
    public static void ValidateAll()
    {
        int issues = ValidateAllControllers(out int totalControllers);
        if (issues == 0)
            Debug.Log($"[AnimatorValidator] scanned {totalControllers} controllers: no broken transitions detected.");
        else
            Debug.LogWarning($"[AnimatorValidator] scanned {totalControllers} controllers: found {issues} suspicious transitions. See console for details.");
    }

    [MenuItem("Assets/Animators/Validate This Controller", true)]
    private static bool ValidateThisController_Validate()
    {
        return Selection.activeObject is AnimatorController;
    }

    [MenuItem("Assets/Animators/Validate This Controller")]
    private static void ValidateThisController()
    {
        if (Selection.activeObject is AnimatorController ctrl)
        {
            var path = AssetDatabase.GetAssetPath(ctrl);
            int issues = ValidateController(ctrl, path);
            if (issues == 0)
                Debug.Log($"[AnimatorValidator] '{path}': no broken transitions detected.");
            else
                Debug.LogWarning($"[AnimatorValidator] '{path}': found {issues} suspicious transitions. See console for details.");
        }
    }

    private static int ValidateController(AnimatorController ctrl, string path)
    {
        int issues = 0;
        for (int i = 0; i < ctrl.layers.Length; i++)
        {
            var layer = ctrl.layers[i];
            issues += ValidateStateMachine(layer.stateMachine, $"{path} > Layer[{i}] '{layer.name}'");
        }
        return issues;
    }

    private static int ValidateStateMachine(AnimatorStateMachine sm, string context)
    {
        int issues = 0;
        // validate state transitions
        foreach (var childState in sm.states)
        {
            var state = childState.state;
            foreach (var t in state.transitions)
            {
                if (!IsValidTransition(t))
                {
                    issues++;
                    Debug.LogError($"[AnimatorValidator] Broken state transition in {context} > State '{state.name}' (no destination; not Exit). Select the controller and remove this transition.");
                }
            }
        }
        // validate Any State transitions
        foreach (var t in sm.anyStateTransitions)
        {
            if (!IsValidTransition(t))
            {
                issues++;
                Debug.LogError($"[AnimatorValidator] Broken AnyState transition in {context} (no destination; not Exit).");
            }
        }
        // validate Entry transitions
        foreach (var t in sm.entryTransitions)
        {
            if (!IsValidTransition(t))
            {
                issues++;
                Debug.LogError($"[AnimatorValidator] Broken Entry transition in {context} (no destination; not Exit).");
            }
        }
        // recurse sub-state machines
        foreach (var childSm in sm.stateMachines)
        {
            issues += ValidateStateMachine(childSm.stateMachine, $"{context} > SubSM '{childSm.stateMachine.name}'");
        }
        return issues;
    }

    private static bool IsValidTransition(AnimatorTransitionBase t)
    {
        if (t == null) return false;
        // Exit transitions are valid even without destination
        if (t.isExit) return true;
        // otherwise must have a destination state or sub-state machine
        if (t.destinationState != null) return true;
        if (t.destinationStateMachine != null) return true;
        return false;
    }
}
#endif
