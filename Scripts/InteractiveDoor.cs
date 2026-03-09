using UnityEngine;
using System.Collections;

public class InteractiveDoor : MonoBehaviour
{
    [Header("Налаштування")]
    public Transform hinge;             // Сюди перетягни об'єкт Hinge
    public Collider blockingCollider;   // Сюди перетягни Door_Visual (його колайдер)
    public bool isOpen = false;         // Стан дверей

    private Quaternion closedRotation;
    private Coroutine animationCoroutine;

    void Start()
    {
        // Запам'ятовуємо початковий стан (закриті)
        closedRotation = hinge.localRotation;

        // Відразу вимикаємо колайдер, якщо двері стартують відкритими
        if (blockingCollider != null)
        {
            blockingCollider.enabled = !isOpen;
        }
    }

    // Unity сама викликає цю функцію, коли ти клікаєш ЛКМ по об'єкту
    void OnMouseDown()
    {
        // Визначаємо, з якого боку клікнули мишкою
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        float openAngle = 90f; // Кут відкриття за замовчуванням

        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            // Напрямок від центру дверей до точки кліку
            Vector3 dirToClick = (hit.point - transform.position).normalized;

            // Скалярний добуток (Dot) показує, точка спереду чи ззаду
            // Якщо Dot > 0 (клікнули спереду), відкриваємо назад (-90)
            // Якщо Dot < 0 (клікнули ззаду), відкриваємо вперед (+90)
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

        // ВАЖЛИВО: Миттєво вимикаємо фізичний колайдер, щоб юніт міг відразу йти!
        if (blockingCollider != null)
        {
            blockingCollider.enabled = !isOpen;
        }

        // Запускаємо плавну анімацію відкриття/закриття
        if (animationCoroutine != null) StopCoroutine(animationCoroutine);

        Quaternion targetRot = isOpen ? closedRotation * Quaternion.Euler(0, angle, 0) : closedRotation;
        animationCoroutine = StartCoroutine(AnimateDoor(targetRot));

        // 🔥 ЛОГІКА ПОДВІЙНИХ ДВЕРЕЙ
        if (!ignoreNeighbors)
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, 1.5f);
            foreach (var hit in hits)
            {
                InteractiveDoor neighbor = hit.GetComponentInParent<InteractiveDoor>();
                // Якщо знайшли інші двері, відкриваємо їх у ту ж саму сторону (передаємо кут angle)
                if (neighbor != null && neighbor != this && neighbor.isOpen != this.isOpen)
                {
                    float neighborAngle = -angle; // дзеркально!
                    neighbor.ToggleDoor(true, neighborAngle);
                }
            }
        }
    }

    // М'яка анімація відкриття
    private IEnumerator AnimateDoor(Quaternion targetRotation)
    {
        float time = 0;
        float duration = 0.3f; // Швидкість відкриття (0.3 секунди)
        Quaternion startRot = hinge.localRotation;

        while (time < duration)
        {
            time += Time.deltaTime;
            // Плавний перехід (Slerp) від поточного кута до цільового
            hinge.localRotation = Quaternion.Slerp(startRot, targetRotation, time / duration);
            yield return null;
        }

        hinge.localRotation = targetRotation; // Фіксуємо точно в кінці
    }
}
