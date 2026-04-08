using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

public class PlayerAnimatorSetup : EditorWindow
{
    private AnimatorController controller;
    private bool addComboStates = true;
    private bool addPowerStates = true;
    
    public static void ShowWindow()
    {
        GetWindow<PlayerAnimatorSetup>("Player Animator Setup");
    }
    
    void OnGUI()
    {
        GUILayout.Label("Player Animator Controller Setup", EditorStyles.boldLabel);
        
        controller = EditorGUILayout.ObjectField("Animator Controller", controller, typeof(AnimatorController), false) as AnimatorController;
        
        if (controller == null)
        {
            EditorGUILayout.HelpBox("Please assign the Animator Controller to set up.", MessageType.Warning);
            
            if (GUILayout.Button("Find AlbularyoAnimations.controller"))
            {
                string[] guids = AssetDatabase.FindAssets("AlbularyoAnimations t:AnimatorController");
                if (guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                }
            }
        }
        
        EditorGUILayout.Space();
        addComboStates = EditorGUILayout.Toggle("Add Combo Attack States", addComboStates);
        addPowerStates = EditorGUILayout.Toggle("Add Stolen Power States", addPowerStates);
        
        EditorGUILayout.Space();
        
        if (controller == null)
        {
            GUI.enabled = false;
        }
        
        if (GUILayout.Button("Setup Animator Controller", GUILayout.Height(30)))
        {
            SetupAnimatorController();
        }
        
        GUI.enabled = true;
        
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("This will add missing parameters and create placeholder states. You'll still need to assign animation clips manually in the Animator window.", MessageType.Info);
    }
    
    void SetupAnimatorController()
    {
        if (controller == null)
        {
            EditorUtility.DisplayDialog("Error", "Please assign an Animator Controller first.", "OK");
            return;
        }
        
        Undo.RecordObject(controller, "Setup Player Animator");
        
        // Get or create base layer
        AnimatorControllerLayer baseLayer = controller.layers[0];
        AnimatorStateMachine stateMachine = baseLayer.stateMachine;
        
        int addedParams = 0;
        int addedStates = 0;
        
        // Add missing parameters
        string[] requiredTriggers = {
            "UnarmedAttack1", "UnarmedAttack2",
            "ArmedAttack1", "ArmedAttack2", "ArmedAttack3"
        };
        
        // Add power triggers
        if (addPowerStates)
        {
            string[] powerTriggers = {
                "AmomongoPrimalSurge",
                "ManananggalBloodboundWings",
                "SigbinShadowVeil",
                "WakwakNightGlide",
                "BerberokaAquaBarrier",
                "PugotCurseField",
                "BusawBoneGrip",
                "CorruptedDiwataSpiritBloom",
                "AswangQueenDaybreakSigil",
                "TiyanakWailOfConfusion",
                "BakunawaOceanWrath",
                "MinokawaRadiantBurst",
                "SarimanokPhoenixBlessing"
            };
            
            System.Array.Resize(ref requiredTriggers, requiredTriggers.Length + powerTriggers.Length);
            System.Array.Copy(powerTriggers, 0, requiredTriggers, requiredTriggers.Length - powerTriggers.Length, powerTriggers.Length);
        }
        
        foreach (string triggerName in requiredTriggers)
        {
            if (!HasParameter(controller, triggerName))
            {
                controller.AddParameter(triggerName, AnimatorControllerParameterType.Trigger);
                addedParams++;
                Debug.Log($"Added parameter: {triggerName}");
            }
        }
        
        // Add missing bool (IsGrounded - optional)
        if (!HasParameter(controller, "IsGrounded"))
        {
            controller.AddParameter("IsGrounded", AnimatorControllerParameterType.Bool);
            addedParams++;
        }
        
        if (addComboStates)
        {
            // Find Idle state
            AnimatorState idleState = FindState(stateMachine, "Idle");
            if (idleState == null)
            {
                EditorUtility.DisplayDialog("Error", "Could not find 'Idle' state. Please ensure your Animator Controller has an Idle state.", "OK");
                return;
            }
            
            // Create combo states
            string[] comboStates = {
                "UnarmedAttack1", "UnarmedAttack2",
                "ArmedAttack1", "ArmedAttack2", "ArmedAttack3"
            };
            
            foreach (string stateName in comboStates)
            {
                if (FindState(stateMachine, stateName) == null)
                {
                    AnimatorState newState = stateMachine.AddState(stateName);
                    addedStates++;
                    Debug.Log($"Created state: {stateName}");
                }
            }
        }
        
        if (addPowerStates)
        {
            // Create power states
            string[] powerStates = {
                "AmomongoPrimalSurge",
                "ManananggalBloodboundWings",
                "SigbinShadowVeil",
                "WakwakNightGlide",
                "BerberokaAquaBarrier",
                "PugotCurseField",
                "BusawBoneGrip",
                "CorruptedDiwataSpiritBloom",
                "AswangQueenDaybreakSigil",
                "TiyanakWailOfConfusion",
                "BakunawaOceanWrath",
                "MinokawaRadiantBurst",
                "SarimanokPhoenixBlessing"
            };
            
            foreach (string stateName in powerStates)
            {
                if (FindState(stateMachine, stateName) == null)
                {
                    AnimatorState newState = stateMachine.AddState(stateName);
                    addedStates++;
                    Debug.Log($"Created state: {stateName}");
                }
            }
        }
        
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        
        EditorUtility.DisplayDialog("Setup Complete", 
            $"Added {addedParams} parameters and {addedStates} states.\n\n" +
            "Next steps:\n" +
            "1. Open Animator window (Window > Animation > Animator)\n" +
            "2. Assign animation clips to the new states\n" +
            "3. Set up transitions following PLAYER_ANIMATOR_SETUP.md guide", 
            "OK");
    }
    
    bool HasParameter(AnimatorController controller, string paramName)
    {
        foreach (var param in controller.parameters)
        {
            if (param.name == paramName) return true;
        }
        return false;
    }
    
    AnimatorState FindState(AnimatorStateMachine stateMachine, string stateName)
    {
        foreach (var state in stateMachine.states)
        {
            if (state.state.name == stateName) return state.state;
        }
        
        // Also check child state machines
        foreach (var childMachine in stateMachine.stateMachines)
        {
            var found = FindState(childMachine.stateMachine, stateName);
            if (found != null) return found;
        }
        
        return null;
    }
}

