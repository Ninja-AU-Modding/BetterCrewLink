using BetterCrewLink.Networking;
using Reactor.Utilities.Attributes;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BetterCrewLink;

[RegisterInIl2Cpp]
public sealed class MicOverlay : MonoBehaviour
{
    private static MicOverlay? _instance;

    private Canvas? _canvas;
    private Image? _background;
    private TextMeshProUGUI? _label;
    private Image? _barFill;
    private RectTransform? _panelRt;
    private OverlayPositionOption _lastPosition;

    public MicOverlay(IntPtr ptr) : base(ptr) { }

    public static MicOverlay Ensure()
    {
        if (_instance != null)
            return _instance;

        var go = new GameObject("BCL_MicOverlay");
        Object.DontDestroyOnLoad(go);
        _instance = go.AddComponent<MicOverlay>();
        _instance.BuildUI();
        return _instance;
    }

    public void SetVisible(bool visible)
    {
        if (_canvas != null)
            _canvas.enabled = visible;
    }

    public void UpdateState(bool talking, float level, string deviceName)
    {
        if (_label == null || _barFill == null)
            return;

        var device = string.IsNullOrWhiteSpace(deviceName) ? "Default" : deviceName;
        _label.text = talking ? $"Mic ({device}): Talking" : $"Mic ({device}): Idle";
        _label.color = talking ? new Color(0.2f, 0.9f, 0.3f) : Color.white;

        var pct = Mathf.Clamp01(level * 10f);
        _barFill.fillAmount = pct;
        _barFill.color = talking ? new Color(0.2f, 0.9f, 0.3f) : new Color(0.6f, 0.6f, 0.6f);
    }

    public void SetPosition(OverlayPositionOption position)
    {
        if (_panelRt == null || position == _lastPosition)
            return;

        _lastPosition = position;

        switch (position)
        {
            case OverlayPositionOption.Left:
                _panelRt.anchorMin = new Vector2(0f, 0.5f);
                _panelRt.anchorMax = new Vector2(0f, 0.5f);
                _panelRt.pivot = new Vector2(0f, 0.5f);
                _panelRt.anchoredPosition = new Vector2(20f, 0f);
                break;
            case OverlayPositionOption.Right:
                _panelRt.anchorMin = new Vector2(1f, 0.5f);
                _panelRt.anchorMax = new Vector2(1f, 0.5f);
                _panelRt.pivot = new Vector2(1f, 0.5f);
                _panelRt.anchoredPosition = new Vector2(-20f, 0f);
                break;
            case OverlayPositionOption.Top:
                _panelRt.anchorMin = new Vector2(0.5f, 1f);
                _panelRt.anchorMax = new Vector2(0.5f, 1f);
                _panelRt.pivot = new Vector2(0.5f, 1f);
                _panelRt.anchoredPosition = new Vector2(0f, -20f);
                break;
            case OverlayPositionOption.Bottom:
                _panelRt.anchorMin = new Vector2(0.5f, 0f);
                _panelRt.anchorMax = new Vector2(0.5f, 0f);
                _panelRt.pivot = new Vector2(0.5f, 0f);
                _panelRt.anchoredPosition = new Vector2(0f, 20f);
                break;
        }
    }

    private void BuildUI()
    {
        var canvasGo = new GameObject("Canvas");
        canvasGo.transform.SetParent(transform, false);

        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 999;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasGo.AddComponent<GraphicRaycaster>();
        SetLayerRecursive(canvasGo, LayerMask.NameToLayer("UI"));

        var panelGo = new GameObject("Panel");
        panelGo.transform.SetParent(canvasGo.transform, false);

        _panelRt = panelGo.AddComponent<RectTransform>();
        _panelRt.sizeDelta = new Vector2(180f, 60f);

        _background = panelGo.AddComponent<Image>();
        _background.color = new Color(0f, 0f, 0f, 0.5f);

        var textGo = new GameObject("Label");
        textGo.transform.SetParent(panelGo.transform, false);
        var textRt = textGo.AddComponent<RectTransform>();
        textRt.anchorMin = new Vector2(0f, 1f);
        textRt.anchorMax = new Vector2(1f, 1f);
        textRt.pivot = new Vector2(0f, 1f);
        textRt.anchoredPosition = new Vector2(8f, -6f);
        textRt.sizeDelta = new Vector2(-16f, 22f);

        _label = textGo.AddComponent<TextMeshProUGUI>();
        _label.fontSize = 16f;
        _label.text = "Mic: Idle";
        if (HudManager.Instance != null && HudManager.Instance.TaskPanel != null)
        {
            _label.font = HudManager.Instance.TaskPanel.taskText.font;
            _label.fontMaterial = HudManager.Instance.TaskPanel.taskText.fontMaterial;
        }

        var barGo = new GameObject("Bar");
        barGo.transform.SetParent(panelGo.transform, false);
        var barRt = barGo.AddComponent<RectTransform>();
        barRt.anchorMin = new Vector2(0f, 0f);
        barRt.anchorMax = new Vector2(1f, 0f);
        barRt.pivot = new Vector2(0f, 0f);
        barRt.anchoredPosition = new Vector2(8f, 8f);
        barRt.sizeDelta = new Vector2(-16f, 12f);

        var barBg = barGo.AddComponent<Image>();
        barBg.color = new Color(1f, 1f, 1f, 0.15f);

        var fillGo = new GameObject("Fill");
        fillGo.transform.SetParent(barGo.transform, false);
        var fillRt = fillGo.AddComponent<RectTransform>();
        fillRt.anchorMin = new Vector2(0f, 0f);
        fillRt.anchorMax = new Vector2(1f, 1f);
        fillRt.pivot = new Vector2(0f, 0.5f);
        fillRt.anchoredPosition = Vector2.zero;
        fillRt.sizeDelta = Vector2.zero;

        _barFill = fillGo.AddComponent<Image>();
        _barFill.type = Image.Type.Filled;
        _barFill.fillMethod = Image.FillMethod.Horizontal;
        _barFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        _barFill.fillAmount = 0f;
        _barFill.color = new Color(0.6f, 0.6f, 0.6f);

        SetPosition(OverlayPositionOption.Left);
    }

    private static void SetLayerRecursive(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
            SetLayerRecursive(child.gameObject, layer);
    }
}
