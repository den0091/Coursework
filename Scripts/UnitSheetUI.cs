using UnityEngine;
using TMPro;
using Firebase.Firestore;
using System.Collections.Generic;

public class UnitSheetUI : MonoBehaviour
{
    [Header("Головна Панель")]
    public GameObject sheetPanel;

    [Header("Поля вводу")]
    public TMP_InputField inputName;
    public TMP_InputField inputPassword;
    public TMP_InputField inputHP;
    public TMP_InputField inputMaxHP;
    public TMP_InputField inputAC;
    public TMP_InputField inputSpeed;
    public TMP_InputField inputNotes;
    public TMP_InputField inputSize; // НОВЕ ПОЛЕ ДЛЯ РОЗМІРУ!

    private string currentTokenID;
    private string worldID;
    private FirebaseFirestore db;

    void Start()
    {
        db = FirebaseFirestore.DefaultInstance;
        worldID = PlayerPrefs.GetString("RoomID", "test_world");

        if (sheetPanel != null) sheetPanel.SetActive(false);
    }

    public void OpenSheet(string tokenID, string currentName, string currentPass, int hp, int maxHp, int ac, int speed, string notes, int size)
    {
        currentTokenID = tokenID;

        inputName.text = currentName;
        inputPassword.text = currentPass;
        inputHP.text = hp.ToString();
        inputMaxHP.text = maxHp.ToString();
        inputAC.text = ac.ToString();
        inputSpeed.text = speed.ToString();
        inputNotes.text = notes;
        if (inputSize != null) inputSize.text = size.ToString(); // Показуємо розмір

        sheetPanel.SetActive(true);
    }

    public void SaveSheet()
    {
        if (string.IsNullOrEmpty(currentTokenID)) return;

        int hp = int.TryParse(inputHP.text, out int h) ? h : 0;
        int maxHp = int.TryParse(inputMaxHP.text, out int mh) ? mh : 0;
        int ac = int.TryParse(inputAC.text, out int a) ? a : 10;
        int speed = int.TryParse(inputSpeed.text, out int s) ? s : 6;

        // Читаємо розмір. Якщо гравець ввів дурницю або 0 - ставимо мінімум 1.
        int size = int.TryParse(inputSize.text, out int sz) ? sz : 1;
        if (size < 1) size = 1;

        Dictionary<string, object> updates = new Dictionary<string, object>
        {
            { "name", inputName.text },
            { "password", inputPassword.text },
            { "hp", hp },
            { "maxHp", maxHp },
            { "ac", ac },
            { "speed", speed },
            { "notes", inputNotes.text },
            { "size", size } // Відправляємо розмір у Firebase
        };

        db.Collection("Worlds").Document(worldID).Collection("Tokens").Document(currentTokenID).UpdateAsync(updates);
        CloseSheet();
    }

    public void CloseSheet()
    {
        sheetPanel.SetActive(false);
        currentTokenID = "";
    }
}