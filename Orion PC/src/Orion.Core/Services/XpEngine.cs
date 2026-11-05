namespace Orion.Core.Services;

public interface IXpEngine
{
    long Step(int level);
    long LevelStart(int level);
    int GetLevel(long xp);
    string GetTitle(int level);
    long CalculateTotalXp(int installedAppsCount, int submissionCount = 0, long bonusXp = 0);
    (int Level, string Title, long CurrentLevelXp, long NextLevelRequiredXp, float ProgressPercent) GetProfileStats(long totalXp);
}

public class XpEngine : IXpEngine
{
    public const int AppInstallXp = 25;
    public const int AppUpdateXp = 10;
    public const int SubmissionXp = 100;

    public long Step(int level) => 50L + 10L * level + (long)level * level;

    public long LevelStart(int level)
    {
        long sum = 0;
        for (int i = 1; i < level; i++)
        {
            sum += Step(i);
        }
        return sum;
    }

    public int GetLevel(long xp)
    {
        int l = 1;
        long left = xp;
        while (left >= Step(l))
        {
            left -= Step(l);
            l++;
        }
        return l;
    }

    public string GetTitle(int level)
    {
        if (level >= 50) return "Gold pilot";
        if (level >= 30) return "Silver pilot";
        if (level >= 10) return "Bronze pilot";
        return "Cadet";
    }

    public long CalculateTotalXp(int installedAppsCount, int submissionCount = 0, long bonusXp = 0)
    {
        return (long)installedAppsCount * AppInstallXp + (long)submissionCount * SubmissionXp + bonusXp;
    }

    public (int Level, string Title, long CurrentLevelXp, long NextLevelRequiredXp, float ProgressPercent) GetProfileStats(long totalXp)
    {
        int level = GetLevel(totalXp);
        string title = GetTitle(level);
        long startXp = LevelStart(level);
        long needed = Step(level);
        long current = totalXp - startXp;
        float progress = needed > 0 ? (float)current / needed : 0f;

        return (level, title, current, needed, Math.Clamp(progress, 0f, 1f));
    }
}

