using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class EnemyTauntIdleController : MonoBehaviour
{
    [Header("Animator")]
    public Animator animator;

    [Tooltip("Layer where Idle and TauntBlend states live.")]
    public int layerIndex = 0;

    [Tooltip("Name of the idle state.")]
    public string idleStateName = "Idle";

    [Tooltip("Name of the taunt blend tree state (mainly for debugging).")]
    public string tauntStateName = "TauntBlend";

    [Header("Parameters")]
    [Tooltip("Trigger that transitions Idle -> TauntBlend.")]
    public string tauntTriggerName = "TauntTrigger";

    [Tooltip("Parameter used by the taunt blend tree to select which taunt to play.")]
    public string tauntIdParameterName = "TauntId";

    [Tooltip("List of taunt IDs to pick from (0, 1, 2... or 0.0, 0.5, 1.0 etc.).")]
    public List<float> tauntIds = new List<float> { 0f, 1f };

    [Header("Timing")]
    [Tooltip("Random idle duration between taunts (seconds).")]
    public Vector2 idleDelayRange = new Vector2(1.0f, 3.0f);

    [Tooltip("If true, first taunt happens as soon as we hit Idle (no wait).")]
    public bool firstTauntImmediate = true;

    private int _idleStateHash;
    private int _tauntStateHash;
    private int _tauntTriggerHash;
    private int _tauntIdParamHash;

    private float _idleTimer = 0f;
    private float _currentIdleTarget = 0f;
    private bool _hasIdleTarget = false;
    private bool _firstTauntDone = false;

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        _idleStateHash = Animator.StringToHash(idleStateName);
        _tauntStateHash = Animator.StringToHash(tauntStateName);
        _tauntTriggerHash = Animator.StringToHash(tauntTriggerName);
        _tauntIdParamHash = Animator.StringToHash(tauntIdParameterName);
    }

    private void OnEnable()
    {
        _idleTimer = 0f;
        _currentIdleTarget = 0f;
        _hasIdleTarget = false;
        _firstTauntDone = false;
    }

    private void Update()
    {
        if (animator == null) return;
        if (!animator.isActiveAndEnabled) return;

        var state = animator.GetCurrentAnimatorStateInfo(layerIndex);
        bool inTransition = animator.IsInTransition(layerIndex);

        bool inIdle =
            !inTransition &&
            (state.shortNameHash == _idleStateHash || state.fullPathHash == _idleStateHash);

        // Only count time while we are in Idle (and not transitioning)
        if (inIdle)
        {
            // If we just entered Idle, set up a new target delay
            if (!_hasIdleTarget)
            {
                if (!firstTauntImmediate || _firstTauntDone)
                {
                    _currentIdleTarget = Random.Range(idleDelayRange.x, idleDelayRange.y);
                }
                else
                {
                    _currentIdleTarget = 0f; // immediate taunt on first time
                }

                _idleTimer = 0f;
                _hasIdleTarget = true;
            }

            _idleTimer += Time.deltaTime;

            if (_idleTimer >= _currentIdleTarget)
            {
                TryTriggerTaunt();
                _hasIdleTarget = false; // wait for next time we are back in Idle
                _firstTauntDone = true;
            }
        }
        else
        {
            // Not in idle: reset so that when we come back to Idle we roll a new delay
            _hasIdleTarget = false;
            _idleTimer = 0f;
        }
    }

    private void TryTriggerTaunt()
    {
        if (tauntIds == null || tauntIds.Count == 0)
        {
            return;
        }

        // Pick random taunt id from list
        float chosenId = tauntIds[Random.Range(0, tauntIds.Count)];

        // Support both int and float parameters
        if (animator.HasParameterOfType(_tauntIdParamHash, AnimatorControllerParameterType.Int))
        {
            animator.SetInteger(_tauntIdParamHash, Mathf.RoundToInt(chosenId));
        }
        else if (animator.HasParameterOfType(_tauntIdParamHash, AnimatorControllerParameterType.Float))
        {
            animator.SetFloat(_tauntIdParamHash, chosenId);
        }

        animator.ResetTrigger(_tauntTriggerHash);
        animator.SetTrigger(_tauntTriggerHash);
    }
}

public static class AnimatorParameterExtensions
{
    public static bool HasParameterOfType(this Animator animator, int nameHash, AnimatorControllerParameterType type)
    {
        if (animator == null) return false;

        foreach (var p in animator.parameters)
        {
            if (p.nameHash == nameHash && p.type == type)
            {
                return true;
            }
        }

        return false;
    }
}
