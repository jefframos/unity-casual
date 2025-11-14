using UnityEngine;

[CreateAssetMenu(
    fileName = "EnemyTypeDefinition",
    menuName = "Game/Enemy Type Definition",
    order = 0)]
public class EnemyTypeDefinition : ScriptableObject
{
    [Header("Identity")]
    public EnemyGrade type;
    public Color color;

    [Header("Presentation")]
    public Sprite icon;         // for UI / HUD / cards, etc.

    [Header("Gameplay")]
    public int value = 100;     // score / reward / whatever
    public float killImpulse = 60f;
    public float mass = 50f;    // desired main mass for this enemy
}
