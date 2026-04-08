using UnityEngine;

public static class EncyclopediaProgressEvents
{
    public static System.Action<string> OnEncounterRecorded;
    public static System.Action<string> OnKillRecorded;

    public static void ReportEncounter(string enemyId)
    {
        if (string.IsNullOrWhiteSpace(enemyId)) return;
        OnEncounterRecorded?.Invoke(enemyId);
    }

    public static void ReportKill(string enemyId)
    {
        if (string.IsNullOrWhiteSpace(enemyId)) return;
        OnKillRecorded?.Invoke(enemyId);
    }

    public static void ReportEncounterAndKill(string enemyId)
    {
        ReportEncounter(enemyId);
        ReportKill(enemyId);
    }
}
