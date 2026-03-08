using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody))]
public class TokenUnit : MonoBehaviour
{
    [Header("Статистика")]
    public string tokenName;
    public string password;
    public int hp;
    public int maxHp;
    public int ac;
    public int speed;
    public string notes;
    public int size = 1;

    [Header("Анімація Руху")]
    public float moveSpeed = 2.5f;
    public float hopHeight = 0.5f;

    private List<Vector3> pathQueue = new List<Vector3>();
    private Coroutine moveCoroutine;

    public bool isPickedUp = false;

    // 🔥 Запам'ятовуємо реальну висоту, щоб ніколи не провалюватися
    private float startY;

    private Rigidbody rb;
    private Collider col;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();

        rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        col = GetComponentInChildren<Collider>();
    }

    public void UpdateStats(string n, string p, int h, int mh, int a, int s, string not, int sz)
    {
        tokenName = n; password = p; hp = h; maxHp = mh; ac = a; speed = s; notes = not; size = sz;
    }

    // Більше не передаємо висоту ззовні, юніт сам знає де стоїть!
    public void SetPickedUp(bool state)
    {
        isPickedUp = state;
        if (state)
        {
            startY = transform.position.y;
            rb.isKinematic = true;
        }
        else
        {
            rb.isKinematic = false;
            transform.rotation = Quaternion.Euler(0, transform.eulerAngles.y, 0);
        }
    }

    void Update()
    {
        if (isPickedUp && pathQueue.Count == 0 && moveCoroutine == null)
        {
            float targetHoverY = startY + 0.5f; // Піднімаємо рівно на півблока

            // 🔥 ДЕТЕКТОР СТЕЛІ: Стріляємо вгору. Якщо дах низько - не піднімаємось!
            if (col != null)
            {
                if (Physics.Raycast(col.bounds.center, Vector3.up, 0.7f))
                {
                    targetHoverY = startY;
                }
            }

            Vector3 targetPos = transform.position;
            targetPos.y = Mathf.Lerp(transform.position.y, targetHoverY, Time.deltaTime * 10f);
            transform.position = targetPos;

            // 🔥 ЛЕГКИЙ ВІТЕРЕЦЬ: М'яке розкачування без підстрибування
            if (targetHoverY > startY + 0.1f)
            {
                float rockX = Mathf.Sin(Time.time * 2.5f) * 0.8f;
                float rockZ = Mathf.Cos(Time.time * 2f) * 0.8f;
                transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(rockX, transform.eulerAngles.y, rockZ), Time.deltaTime * 5f);
            }
            else
            {
                // Якщо притиснутий дахом - стоїть струнко
                transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(0, transform.eulerAngles.y, 0), Time.deltaTime * 5f);
            }
        }
        else if (!isPickedUp && pathQueue.Count == 0 && moveCoroutine == null)
        {
            transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(0, transform.eulerAngles.y, 0), Time.deltaTime * 10f);
        }
    }

    public void PlayBonkAnimation(Vector3 obstaclePos)
    {
        isPickedUp = false;
        if (moveCoroutine != null) StopCoroutine(moveCoroutine);
        moveCoroutine = StartCoroutine(BonkRoutine(obstaclePos));
    }

    private IEnumerator BonkRoutine(Vector3 badTarget)
    {
        rb.isKinematic = true;
        Vector3 startPos = transform.position;
        Vector3 dir = (badTarget - startPos).normalized;
        dir.y = 0;
        Vector3 hitPos = startPos + dir * 0.35f;

        float duration = 0.12f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            transform.position = Vector3.Lerp(startPos, hitPos, elapsed / duration);
            yield return null;
        }

        elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            transform.position = Vector3.Lerp(hitPos, startPos, elapsed / duration);
            yield return null;
        }

        transform.position = startPos;
        rb.isKinematic = false;
        moveCoroutine = null;
    }

    public void MoveAlongPath(List<Vector3> newPath)
    {
        if (newPath == null || newPath.Count == 0) return;
        pathQueue = new List<Vector3>(newPath);
        if (moveCoroutine != null) StopCoroutine(moveCoroutine);
        moveCoroutine = StartCoroutine(FollowPathRoutine());
    }

    private IEnumerator FollowPathRoutine()
    {
        rb.isKinematic = true;

        float bodyOffset = col != null ? col.bounds.extents.y : (size * 0.5f);

        while (pathQueue.Count > 0)
        {
            Vector3 startPos = transform.position;
            Vector3 targetFloor = pathQueue[0];
            pathQueue.RemoveAt(0);

            Vector3 targetVisualPos = new Vector3(targetFloor.x, targetFloor.y + bodyOffset + 0.1f, targetFloor.z);

            float distanceFlat = Vector3.Distance(new Vector3(startPos.x, 0, startPos.z), new Vector3(targetVisualPos.x, 0, targetVisualPos.z));
            if (distanceFlat < 0.05f)
            {
                transform.position = targetVisualPos;
                continue;
            }

            float duration = Mathf.Max(0.4f, distanceFlat / moveSpeed);
            float elapsed = 0f;

            float startFloorY = startPos.y - bodyOffset;
            bool isHeightChange = Mathf.Abs(targetFloor.y - startFloorY) > 0.1f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float smoothT = Mathf.SmoothStep(0f, 1f, t);

                Vector3 currentPos = Vector3.Lerp(startPos, targetVisualPos, smoothT);

                if (isHeightChange) currentPos.y += Mathf.Sin(t * Mathf.PI) * hopHeight;
                else currentPos.y += Mathf.Sin(t * Mathf.PI * 2f) * 0.05f;

                transform.position = currentPos;

                Vector3 dir = (targetVisualPos - startPos).normalized;
                dir.y = 0;
                if (dir.magnitude > 0.01f)
                {
                    // 🔥 Зменшено нахил при кроках
                    float walkWobble = Mathf.Sin(elapsed * 12f) * 0.5f;
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 10f) * Quaternion.Euler(walkWobble, 0, 0);
                }

                yield return null;
            }
            transform.position = targetVisualPos;
        }

        moveCoroutine = null;
        transform.rotation = Quaternion.Euler(0, transform.eulerAngles.y, 0);
        rb.isKinematic = false;
    }
}