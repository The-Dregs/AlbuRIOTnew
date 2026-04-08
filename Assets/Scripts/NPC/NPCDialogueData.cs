using UnityEngine;

[CreateAssetMenu(fileName = "NewNPCDialogue", menuName = "Dialogue/NPC Dialogue Data")]
public class NPCDialogueData : ScriptableObject
{
    [System.Serializable]
    public class Line
    {
        public string speaker;
        [TextArea] public string text;
        [Tooltip("Optional sound clip to play when this dialogue line is shown")]
        public AudioClip soundClip;
    }

    public Line[] lines;
}
