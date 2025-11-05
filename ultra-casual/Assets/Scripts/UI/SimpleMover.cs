using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("FX/Simple Mover")]
public class SimpleMover : MonoBehaviour
{
    public enum MoveMode
    {
        ZigZag,     // back and forth smoothly
        Loop        // move to target, teleport back, repeat
    }

    [Header("Movement Settings")]
    [Tooltip("Local-space offset from the starting position.")]
    public Vector3 offset = new Vector3(0f, 1f, 0f);

    [Tooltip("Time in seconds for a full forward (and back if ZigZag).")]
    public float duration = 2f;

    [Tooltip("Type of looping movement.")]
    public MoveMode moveMode = MoveMode.ZigZag;

    [Tooltip("If true, uses local position instead of world position.")]
    public bool useLocalPosition = true;

    [Tooltip("Optional curve to shape motion (time 0–1).")]
    public AnimationCurve motionCurve = AnimationCurve.Linear(0, 0, 1, 1);

    private Vector3 _startPos;
    private float _timer;

    private void Awake()
    {
        _startPos = useLocalPosition ? transform.localPosition : transform.position;
    }

    private void OnEnable()
    {
        _timer = 0f;
    }

    private void Update()
    {
        if (duration <= 0f) return;

        _timer += Time.deltaTime;
        float t = _timer / duration;

        switch (moveMode)
        {
            case MoveMode.ZigZag:
                // ping-pong between 0 and 1
                t = Mathf.PingPong(_timer / duration, 1f);
                break;

            case MoveMode.Loop:
                // when we reach the end, reset timer to restart instantly
                if (_timer >= duration)
                {
                    _timer = 0f;
                    if (useLocalPosition)
                        transform.localPosition = _startPos;
                    else
                        transform.position = _startPos;
                }
                break;
        }

        float curvedT = motionCurve.Evaluate(t);
        Vector3 newPos = Vector3.LerpUnclamped(_startPos, _startPos + offset, curvedT);

        if (useLocalPosition)
            transform.localPosition = newPos;
        else
            transform.position = newPos;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        duration = Mathf.Max(duration, 0.01f);
    }
#endif
}
