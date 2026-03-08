using UnityEngine;

public class InteractiveTorch : MonoBehaviour
{
    [Header("Що вимикати?")]
    public GameObject flameVisual; // Сюди перетягни об'єкт FlameVisual (який містить світло)
    public bool isLit = true;

    void Start()
    {
        UpdateTorchState();
    }

    void OnMouseDown()
    {
        isLit = !isLit;
        UpdateTorchState();
    }

    private void UpdateTorchState()
    {
        if (flameVisual != null)
        {
            // Просто вмикаємо/вимикаємо вогник і світло разом із ним
            flameVisual.SetActive(isLit);
        }
    }
}