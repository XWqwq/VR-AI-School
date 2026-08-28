using System.Text;
using UnityEngine;

public sealed class CampusExperienceReportService : MonoBehaviour
{
    public static CampusExperienceReportService Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public string BuildLocalReport()
    {
        var progress = CampusProgressService.Instance;
        if (progress == null)
            return "探索数据尚未初始化，请稍后再试。";

        var data = progress.Data;
        var completed = data.completedLocationIds != null ? data.completedLocationIds.Count : 0;
        var secrets = data.unlockedSecretIds != null ? data.unlockedSecretIds.Count : 0;
        var stamps = 0;
        foreach (var location in data.locations)
            if (location.stampAwarded) stamps++;

        var profile = CampusUserProfileService.Instance;
        var interest = profile != null && profile.HasInterest ? profile.InterestName : "尚未选择";
        var style = ResolveStyle(completed, data.questionCount, secrets);
        var next = ResolveNextSuggestion(data);

        var name = profile != null && profile.HasIdentity ? profile.StudentName : "新同学";
        var major = profile != null && profile.HasIdentity ? profile.StudentMajor : "专业尚未录入";
        var mainStampCount = CountMainlineStamps(progress);
        var report = new StringBuilder();
        report.AppendLine("致 " + name + " · 我的第一日");
        report.AppendLine(major + "专业  |  " + style);
        report.AppendLine("主线印章：" + BuildMainlineStampLine(progress));
        report.AppendLine("参观场景：" + data.visitedScenes.Count + "/3   AI 提问：" + data.questionCount + " 次");
        report.AppendLine("全部印章：" + stamps + "   隐藏发现：" + secrets + "/3");
        report.AppendLine();
        report.AppendLine(mainStampCount >= 3
            ? "今日寄语：三枚印章已经点亮。从这里出发，让好奇心带你走向更大的世界。"
            : "今日寄语：每一次停留和提问，都是认识校园的新起点。");
        report.AppendLine("下一步：" + next);
        return report.ToString().TrimEnd();
    }

    private static int CountMainlineStamps(CampusProgressService progress)
    {
        var count = 0;
        if (progress.HasStamp("main_future_tech_center")) count++;
        if (progress.HasStamp("main_teaching_center")) count++;
        if (progress.HasStamp("main_dongwu_gate")) count++;
        return count;
    }

    private static string BuildMainlineStampLine(CampusProgressService progress)
    {
        return Stamp(progress, "main_future_tech_center", "科创") + "  " +
               Stamp(progress, "main_teaching_center", "教学") + "  " +
               Stamp(progress, "main_dongwu_gate", "东吴门");
    }

    private static string Stamp(CampusProgressService progress, string id, string label)
    {
        return progress.HasStamp(id) ? "●" + label : "○" + label;
    }

    public string BuildAIReportQuestion()
    {
        return "请根据以下虚拟校园参观记录，为新生写一段第一日专属寄语：\n" + BuildLocalReport() +
               "\n要求：称呼新生姓名；结合专业、印章和参观记录；语气温暖、有未来感；不要虚构学校事实；不超过120个汉字。";
    }

    private static string ResolveStyle(int completed, int questions, int secrets)
    {
        if (secrets >= 2) return "细节发现型探索者";
        if (questions >= 3) return "主动求知型探索者";
        if (completed >= 3) return "行动体验型探索者";
        return "校园初识者";
    }

    private static string ResolveNextSuggestion(CampusProgressData data)
    {
        if (!data.visitedScenes.Contains(1))
            return "打开校园地图，选择“智慧教室”，通过传送门前往参观。";
        if (!data.visitedScenes.Contains(2))
            return "打开校园地图，选择“AI 展厅”，了解校园智能应用。";
        if (data.questionCount == 0)
            return "打开“AI 校园向导”，选择“猜你想问”，完成第一次校园提问。";
        if (data.unlockedSecretIds == null || data.unlockedSecretIds.Count < 3)
            return "在感兴趣的地点持续观察，寻找尚未解锁的隐藏记录。";
        return "你已完成核心体验，可以按兴趣方向向 AI 查询课程和校园资源。";
    }
}
