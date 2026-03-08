using UnityEngine;

public class PhysicalDice : MonoBehaviour
{
    [Header("Точки на гранях (від 1 до 6)")]
    public Transform[] faceTransforms;
    public int[] faceValues = { 1, 2, 3, 4, 5, 6 };

    private Rigidbody rb;
    private bool isRolling = false;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void Update()
    {
        // Перевіряємо, чи кубик зупинився
        if (isRolling && rb.linearVelocity.magnitude < 0.01f && rb.angularVelocity.magnitude < 0.01f)
        {
            isRolling = false;
            ReadDiceFace();
        }

        // Для тесту: Кидаємо кубик на пробіл!
        if (Input.GetKeyDown(KeyCode.Space))
        {
            RollDice();
        }
    }

    public void RollDice()
    {
        // Підкидаємо вгору і задаємо випадкове обертання
        rb.AddForce(Vector3.up * 5f, ForceMode.Impulse);
        rb.AddTorque(Random.insideUnitSphere * 10f, ForceMode.Impulse);
        isRolling = true;
    }

    void ReadDiceFace()
    {
        int result = 0;
        float maxY = -Mathf.Infinity;

        // Шукаємо, яка грань дивиться в небо
        for (int i = 0; i < faceTransforms.Length; i++)
        {
            if (faceTransforms[i].position.y > maxY)
            {
                maxY = faceTransforms[i].position.y;
                result = faceValues[i];
            }
        }

        Debug.Log($"<color=cyan>[Кубик] Випало: {result}!</color>");

        // НОВЕ: Відправляємо результат прямо в наш чат усім гравцям!
        ChatManager chat = FindFirstObjectByType<ChatManager>();
        if (chat != null)
        {
            chat.SendLog($"[Кубик] Кидок: випало <b>{result}</b>!");
        }
    }
}