using UnityEngine;

[CreateAssetMenu(fileName = "New Tutorial", menuName = "AlbuRIOT/Tutorial Data")]
public class TutorialData : ScriptableObject
{
    [Header("Tutorial Information")]
    public string tutorialName;
    [TextArea] public string description;
    public int tutorialID;

    [Header("Dialogue")]
    public GameObject dialoguePanel; // The dialogue panel to show
    public GameObject continuePrompt; // Continue prompt for this dialogue
    [TextArea(3, 5)] public string dialogueText; // Optional: text content (if panel has TextMeshPro)

    [Header("UI Actions")]
    [Tooltip("Enable health bar UI for the triggering player")]
    public bool enableHealthBar = false;
    [Tooltip("Enable skill UI for the triggering player")]
    public bool enableSkillUI = false;
    [Tooltip("Show quest UI (global, but only for triggering player)")]
    public bool showQuestUI = false;
    public GameObject questUI; // Optional: specific quest UI to show

    [Header("Settings")]
    [Tooltip("Delay before showing continue prompt")]
    public float continueDelay = 1.2f;
    [Tooltip("One-shot: disable trigger after first use")]
    public bool oneShot = true;
    [Tooltip("Allow skipping with input")]
    public bool allowSkip = true;
}
