using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public sealed class AdmissionLetterMenuButton : MonoBehaviour
{
    private AdmissionLetterGuide owner;
    private Image background;
    private string actionId;
    private Color normalColor;
    private bool isHovered;
    private Vector3 normalScale = Vector3.one;
    private Coroutine pressRoutine;
    private readonly Color hoverColor = new Color(0.2f, 0.78f, 1f, 1f);

    public void Configure(
        AdmissionLetterGuide menuOwner, string id, Image image, Color baseColor)
    {
        owner = menuOwner;
        actionId = id;
        background = image;
        normalColor = baseColor;
        normalScale = transform.localScale;
        SetHovered(false);
    }

    public void SetHovered(bool hovered)
    {
        if (hovered && !isHovered)
            CampusFeedbackService.Instance?.Hover(true);
        isHovered = hovered;
        if (background != null)
            background.color = hovered ? hoverColor : normalColor;
    }

    public void Activate()
    {
        CampusFeedbackService.Instance?.Confirm(true);
        if (pressRoutine != null)
            StopCoroutine(pressRoutine);
        pressRoutine = StartCoroutine(PlayPressFeedback());
        owner?.HandleMenuAction(actionId);
    }

    public void ResetVisual()
    {
        isHovered = false;
        transform.localScale = normalScale;
        if (background != null)
            background.color = normalColor;
    }

    private IEnumerator PlayPressFeedback()
    {
        transform.localScale = normalScale * 0.94f;
        if (background != null)
            background.color = new Color(1f, 0.78f, 0.22f, 1f);
        yield return new WaitForSecondsRealtime(0.12f);
        transform.localScale = normalScale;
        if (background != null)
            background.color = isHovered ? hoverColor : normalColor;
        pressRoutine = null;
    }

    private void OnDisable()
    {
        if (pressRoutine != null)
        {
            StopCoroutine(pressRoutine);
            pressRoutine = null;
        }
        ResetVisual();
    }
}
