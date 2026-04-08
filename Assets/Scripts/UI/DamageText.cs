using UnityEngine;
using TMPro;

public class DamageText : MonoBehaviour
{
    [Header("Animation")]
    public float floatSpeed = 2f;
    public float fadeDuration = 1f;
    
    private TextMeshPro textMesh;
    private Color originalColor;
    private float timer = 0f;

    public void ShowDamage(int damage)
    {
        if (textMesh == null) textMesh = GetComponent<TextMeshPro>();
        if (textMesh != null)
        {
            Debug.Log($"[DamageText] Showing damage: {damage}, setting text to '{damage.ToString()}'");
            textMesh.text = damage.ToString();
            originalColor = textMesh.color;
            timer = 0f;
        }
        else
        {
            Debug.LogError("[DamageText] TextMeshPro component is null! Cannot display damage.");
        }
    }

    void Awake()
    {
        textMesh = GetComponent<TextMeshPro>();
        if (textMesh != null)
        {
            // Don't capture color in Awake - it should be set by the prefab
            // originalColor will be captured in ShowDamage
        }
    }

    void Update()
    {
        if (textMesh == null) return;
        
        transform.position += Vector3.up * floatSpeed * Time.deltaTime;
        
        // Make the text always face the camera
        if (Camera.main != null)
        {
            transform.LookAt(transform.position + Camera.main.transform.rotation * Vector3.forward,
                            Camera.main.transform.rotation * Vector3.up);
        }
        
        timer += Time.deltaTime;
        float alpha = Mathf.Lerp(originalColor.a, 0, timer / fadeDuration);
        textMesh.color = new Color(originalColor.r, originalColor.g, originalColor.b, alpha);
        
        if (timer >= fadeDuration)
        {
            Destroy(gameObject);
        }
    }
}
