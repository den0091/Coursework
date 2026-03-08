using UnityEngine;
using TMPro;
using Firebase.Firestore;
using Firebase.Extensions;
using System.Collections.Generic;
using UnityEngine.UI; // Потрібно для ScrollRect

public class ChatManager : MonoBehaviour
{
    [Header("UI Чату")]
    public GameObject chatPanel; // НОВЕ: Сама темна панель чату
    public TMP_Text chatDisplay;
    public TMP_InputField chatInput;
    public ScrollRect scrollRect; // НОВЕ: Для автопрокрутки вниз

    private FirebaseFirestore db;
    private string worldID;
    private ListenerRegistration chatListener;
    private string playerName;

    void Start()
    {
        db = FirebaseFirestore.DefaultInstance;
        worldID = PlayerPrefs.GetString("RoomID", "test_world");

        if (PlayerPrefs.GetInt("IsMaster", 0) == 1) playerName = "Майстер";
        else
        {
            playerName = PlayerPrefs.GetString("MyTokenPassword", "Гравець");
            if (string.IsNullOrEmpty(playerName)) playerName = "Гравець";
        }

        ListenToChat();
    }

    public void SendChatMessage()
    {
        if (chatInput != null && !string.IsNullOrEmpty(chatInput.text))
        {
            SendLog($"<b>[{playerName}]:</b> {chatInput.text}");
            chatInput.text = "";
        }
    }

    public void SendLog(string message)
    {
        var data = new Dictionary<string, object>
        {
            { "text", message },
            { "timestamp", FieldValue.ServerTimestamp }
        };
        db.Collection("Worlds").Document(worldID).Collection("Chat").AddAsync(data);
    }

    void ListenToChat()
    {
        chatListener = db.Collection("Worlds").Document(worldID).Collection("Chat")
            .OrderBy("timestamp").LimitToLast(15)
            .Listen(snap =>
            {
                string fullChat = "";
                foreach (var doc in snap.Documents)
                {
                    if (doc.ContainsField("text"))
                    {
                        fullChat += doc.GetValue<string>("text") + "\n";
                    }
                }
                if (chatDisplay != null) chatDisplay.text = fullChat;

                // Автоматично крутимо чат у самий низ при новому повідомленні
                Invoke("ScrollToBottom", 0.1f);
            });
    }

    void ScrollToBottom()
    {
        if (scrollRect != null) scrollRect.verticalNormalizedPosition = 0f;
    }

    // НОВЕ: Функція для кнопки Відкрити/Закрити чат
    public void ToggleChat()
    {
        if (chatPanel != null)
        {
            chatPanel.SetActive(!chatPanel.activeSelf);
        }
    }

    void OnDestroy()
    {
        chatListener?.Stop();
    }
}