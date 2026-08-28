using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class CampusPhotoService : MonoBehaviour
{
    public static CampusPhotoService Instance { get; private set; }
    public bool IsCapturing { get; private set; }
    public string LastPhotoPath { get; private set; } = string.Empty;

    private TextMesh statusText;
    private Transform head;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildStatusDisplay();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void RequestDongwuGatePhoto()
    {
        if (IsCapturing) return;
        var progress = CampusProgressService.Instance;
        if (SceneManager.GetActiveScene().buildIndex != 0 || progress == null ||
            !progress.HasStamp("main_dongwu_gate"))
        {
            StartCoroutine(ShowTemporary(
                "还不能拍摄\n请先回到主校园，找到东吴门标记并完成注视打卡。", 6f));
            return;
        }

        var confirmation = CampusAIActionConfirmationService.Instance;
        if (confirmation == null) return;
        confirmation.Request(
            "拍摄东吴门头显视角纪念照。确认后通知书会自动收起，请面向东吴门站稳并保持头部不动。系统倒数 3 秒后自动拍摄，照片只保存在本机。",
            () => StartCoroutine(CaptureRoutine()));
    }

    private IEnumerator CaptureRoutine()
    {
        IsCapturing = true;
        AdmissionLetterGuide.Instance?.CloseForPhoto();
        yield return new WaitForSecondsRealtime(0.7f);
        for (var count = 3; count >= 1; count--)
        {
            SetStatus("纪念照准备\n" + count + "\n请面向东吴门，保持头部不动");
            CampusFeedbackService.Instance?.Hover();
            yield return new WaitForSecondsRealtime(1f);
        }
        SetStatus("正在拍摄……");
        yield return new WaitForEndOfFrame();
        statusText.gameObject.SetActive(false);
        yield return new WaitForEndOfFrame();

        var folder = Path.Combine(Application.persistentDataPath, "VirtualCampusPhotos");
        Directory.CreateDirectory(folder);
        LastPhotoPath = Path.Combine(folder, "东吴门_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
        ScreenCapture.CaptureScreenshot(LastPhotoPath, 1);
        yield return new WaitForSecondsRealtime(0.8f);
        CampusFeedbackService.Instance?.TaskComplete();
        SetStatus("拍摄完成\n照片已保存在本机“VirtualCampusPhotos”文件夹\n可继续探索，或打开通知书查看“我的第一日”");
        yield return new WaitForSecondsRealtime(6f);
        statusText.gameObject.SetActive(false);
        IsCapturing = false;
    }

    private IEnumerator ShowTemporary(string message, float duration)
    {
        SetStatus(message);
        yield return new WaitForSecondsRealtime(duration);
        if (!IsCapturing) statusText.gameObject.SetActive(false);
    }

    private void SetStatus(string message)
    {
        if (head == null) head = Camera.main != null ? Camera.main.transform : null;
        if (head != null)
        {
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.1f) forward = Vector3.forward;
            statusText.transform.position = head.position + forward * 1.25f + Vector3.up * 0.22f;
            statusText.transform.rotation = Quaternion.LookRotation(statusText.transform.position - head.position, Vector3.up);
        }
        statusText.text = message;
        statusText.gameObject.SetActive(true);
    }

    private void BuildStatusDisplay()
    {
        var display = new GameObject("Campus Photo Status");
        display.transform.SetParent(transform, false);
        statusText = display.AddComponent<TextMesh>();
        statusText.anchor = TextAnchor.MiddleCenter;
        statusText.alignment = TextAlignment.Center;
        statusText.fontSize = 62;
        statusText.characterSize = 0.012f;
        statusText.color = new Color(1f, 0.82f, 0.3f, 1f);
        display.SetActive(false);
    }
}
