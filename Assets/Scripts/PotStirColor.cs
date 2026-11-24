using UnityEngine;


[RequireComponent(typeof(Renderer))]
public class PotStirColor : MonoBehaviour
{
    [SerializeField] Color startBlue = new(0.05f, 0.25f, 0.8f);
    [SerializeField] Color darkBlue = new(0.02f, 0.05f, 0.25f);
    [SerializeField] Color completedGreen = new(0.05f, 0.8f, 0.15f);
    [SerializeField] bool showDebugLogs;

    Renderer rendererComponent;
    MaterialPropertyBlock propertyBlock;

    [SerializeField] Conductor conductor;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    void Awake()
    {
        rendererComponent = GetComponent<Renderer>();
        propertyBlock = new MaterialPropertyBlock();
        ResetColor();
    }

    public void SetStirProgress(float progress)
    {
        if (showDebugLogs)
            Debug.Log($"PotStirColor: Set progress to {progress:P0}.", this);
        Apply(Color.Lerp(startBlue, darkBlue, Mathf.Clamp01(progress)));
    }

    public void CompleteStir()
    {
        if (showDebugLogs)
            Debug.Log("PotStirColor: Stir completed; changing to green.", this);
        Apply(completedGreen);

        if (conductor != null) conductor.RegisterAction(ActionType.Stir);
    }

    public void ResetColor()
    {
        Apply(Color.teal);
    }

    void Apply(Color color)
    {
        rendererComponent.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(BaseColorId, color);
        propertyBlock.SetColor(ColorId, color);
        rendererComponent.SetPropertyBlock(propertyBlock);
    }
}
