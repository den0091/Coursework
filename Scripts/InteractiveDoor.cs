using UnityEngine;
using System.Collections;

public class InteractiveDoor : MonoBehaviour
{
    [Header("Налаштування")]
    public Transform hinge;
    public Collider blockingCollider;
    public bool isOpen = false;

    private Quaternion closedRotation;
    private Coroutine animationCoroutine;

    void Start()
    {
        closedRotation = hinge.localRotation;

        if (blockingCollider != null)
        {
            blockingCollider.enabled = !isOpen;
        }
    }

    void OnMouseDown()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        float openAngle = 90f;

        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            Vector3 dirToClick = (hit.point - transform.position).normalized;

            if (Vector3.Dot(transform.forward, dirToClick) > 0)
            {
                openAngle = -90f;
            }
            else
            {
                openAngle = 90f;
            }
        }

        ToggleDoor(false, openAngle);
    }

    public void ToggleDoor(bool ignoreNeighbors, float angle = 90f)
    {
        isOpen = !isOpen;

        if (blockingCollider != null)
        {
            blockingCollider.enabled = !isOpen;
        }

        if (animationCoroutine != null) StopCoroutine(animationCoroutine);

        Quaternion targetRot = isOpen ? closedRotation * Quaternion.Euler(0, angle, 0) : closedRotation;
        animationCoroutine = StartCoroutine(AnimateDoor(targetRot));

        // ✅ ВИПРАВЛЕНА ЛОГІКА ПОДВІЙНИХ ДВЕРЕЙ
        if (!ignoreNeighbors)
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, 1.5f);
            foreach (var hit in hits)
            {
                InteractiveDoor neighbor = hit.GetComponentInParent<InteractiveDoor>();

                if (neighbor != null && neighbor != this && neighbor.isOpen != this.isOpen)
                {
                    // 🔧 НОВА ЛОГІКА: Визначаємо чи двері дивляться в один бік
                    float directionDot = Vector3.Dot(transform.forward, neighbor.transform.forward);

                    float neighborAngle;

                    if (directionDot > 0.5f)
                    {
                        // Двері дивляться майже в один бік → той самий кут
                        neighborAngle = angle;
                    }
                    else if (directionDot < -0.5f)
                    {
                        // Двері дивляться в ПРОТИЛЕЖНІ боки → інвертуємо кут!
                        neighborAngle = -angle;
                    }
                    else
                    {
                        // Двері перпендикулярні → також інвертуємо
                        neighborAngle = -angle;
                    }

                    neighbor.ToggleDoor(true, neighborAngle);
                }
            }
        }
    }

    private IEnumerator AnimateDoor(Quaternion targetRotation)
    {
        float time = 0;
        float duration = 0.3f;
        Quaternion startRot = hinge.localRotation;

        while (time < duration)
        {
            time += Time.deltaTime;
            hinge.localRotation = Quaternion.Slerp(startRot, targetRotation, time / duration);
            yield return null;
        }

        hinge.localRotation = targetRotation;
    }
}
