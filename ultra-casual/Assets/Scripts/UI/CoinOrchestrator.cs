using System.Collections.Generic;
using UnityEngine;
using TMPro;

[DisallowMultipleComponent]
public class CoinOrchestrator : MonoBehaviour
{
    public static CoinOrchestrator Instance { get; private set; }

    [Header("Refs")]
    public RectTransform canvasRect;
    public Camera uiCamera; // Optional. If null, uses Camera.main.

    [Header("Pool")]
    public TextMeshProUGUI coinLabelPrefab;
    public int initialPool = 8;
    public int maxExtra = 32;

    [Header("Anim")]
    public float duration = 0.8f;
    public Vector2 moveUp = new Vector2(0f, 80f);
    public AnimationCurve alphaCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);
    public AnimationCurve scaleCurve = AnimationCurve.EaseInOut(0, 0.9f, 1, 1.15f);

    private readonly Queue<TextMeshProUGUI> _pool = new Queue<TextMeshProUGUI>();
    private readonly List<ActivePop> _active = new List<ActivePop>();
    private int _spawnedCount;
    private Canvas _canvas;

    private struct ActivePop
    {
        public TextMeshProUGUI label;
        public RectTransform rect;
        public Vector2 startPos;
        public Vector2 endPos;
        public float t; // 0..1
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (canvasRect == null)
        {
            canvasRect = GetComponent<RectTransform>();
        }

        _canvas = GetComponentInParent<Canvas>();
        WarmPool();
    }

    private void WarmPool()
    {
        if (coinLabelPrefab == null || canvasRect == null) return;
        for (int i = 0; i < initialPool; i++)
        {
            _pool.Enqueue(NewItem());
        }
    }

    private TextMeshProUGUI NewItem()
    {
        var inst = Instantiate(coinLabelPrefab, canvasRect);
        inst.gameObject.SetActive(false);
        _spawnedCount++;
        return inst;
    }

    private TextMeshProUGUI Get()
    {
        if (_pool.Count > 0) return _pool.Dequeue();

        if (maxExtra <= 0 || _spawnedCount < initialPool + maxExtra)
        {
            return NewItem();
        }

        if (_active.Count > 0)
        {
            var recycle = _active[0];
            _active.RemoveAt(0);
            recycle.label.gameObject.SetActive(false);
            return recycle.label;
        }

        return NewItem();
    }

    private void Return(TextMeshProUGUI label)
    {
        if (label == null) return;
        label.gameObject.SetActive(false);
        _pool.Enqueue(label);
    }

    /// <summary>
    /// Spawn popup for amount at a fixed world position (no following).
    /// </summary>
    public void PopCoinsAt(int amount, Vector3 worldPos)
    {
        if (coinLabelPrefab == null || canvasRect == null) return;

        Vector2 localPos;
        if (!WorldToLocalCanvasPoint(worldPos, out localPos)) return;

        var label = Get();
        var rect = label.rectTransform;

        label.text = amount >= 0 ? $"+{amount}" : amount.ToString();
        label.alpha = 1f;
        rect.localScale = Vector3.one;
        rect.anchoredPosition = localPos;
        label.gameObject.SetActive(true);

        var pop = new ActivePop
        {
            label = label,
            rect = rect,
            startPos = localPos,
            endPos = localPos + moveUp,
            t = 0f
        };
        _active.Add(pop);
    }

    private bool WorldToLocalCanvasPoint(Vector3 world, out Vector2 local)
    {
        local = Vector2.zero;

        // Decide camera based on canvas mode:
        // - Overlay: screen cam must be null for ScreenPointToLocalPointInRectangle
        // - Camera/World: use uiCamera or Camera.main
        var effectiveCam = uiCamera != null ? uiCamera : Camera.main;

        Vector2 screen;
        if (_canvas != null && _canvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            // For overlay, screen-point uses Camera.main but ScreenPointToLocal expects null cam.
            Vector3 s = (effectiveCam != null)
                ? effectiveCam.WorldToScreenPoint(world)
                : new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);

            screen = new Vector2(s.x, s.y);
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out local);
        }
        else
        {
            // Screen Space - Camera or World Space
            Vector3 s = RectTransformUtility.WorldToScreenPoint(effectiveCam, world);
            screen = new Vector2(s.x, s.y);
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, effectiveCam, out local);
        }
    }

    private void Update()
    {
        if (_active.Count == 0) return;

        float dt = Time.unscaledDeltaTime;
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var p = _active[i];
            p.t += dt / Mathf.Max(0.0001f, duration);
            float t01 = Mathf.Clamp01(p.t);

            p.rect.anchoredPosition = Vector2.LerpUnclamped(p.startPos, p.endPos, t01);

            float a = alphaCurve.Evaluate(t01);
            float s = scaleCurve.Evaluate(t01);
            p.label.alpha = a;
            p.rect.localScale = Vector3.one * s;

            if (t01 >= 1f)
            {
                Return(p.label);
                _active.RemoveAt(i);
            }
            else
            {
                _active[i] = p;
            }
        }
    }
}
