using TMPro;
using UnityEngine;

// Score, combo and multiplier readout. Put it on a panel at one end of the ticket rail.
public class ScoreView : MonoBehaviour
{
    [SerializeField] ScoreKeeper score;
    [SerializeField] TMP_Text scoreText;
    [SerializeField] TMP_Text comboText;
    [SerializeField] TMP_Text multiplierText;

    [Header("Feel")]
    [SerializeField] float countUpSharpness = 12f; // how fast the shown score catches up
    [SerializeField] float punchScale = 1.25f;     // combo/multiplier pop on change
    [SerializeField] float recoverSharpness = 10f;
    [SerializeField] Color comboColor = Color.white;
    [SerializeField] Color comboBrokenColor = new(0.9f, 0.3f, 0.3f);

    float shownScore;
    int shownScoreInt = -1;
    int lastCombo, lastMultiplier = 1;

    void OnEnable()
    {
        if (score == null) return;
        score.Changed += Refresh;
        score.ComboBroken += OnComboBroken;
        Refresh();
    }

    void OnDisable()
    {
        if (score == null) return;
        score.Changed -= Refresh;
        score.ComboBroken -= OnComboBroken;
    }

    void Refresh()
    {
        if (score.Score == 0) shownScore = 0; // new song: don't count down from the last one

        comboText.text = score.Combo > 0 ? $"{score.Combo} combo" : "";
        multiplierText.text = $"x{score.Multiplier}";

        if (score.Combo > lastCombo) Punch(comboText.transform);
        if (score.Multiplier > lastMultiplier) Punch(multiplierText.transform);
        lastCombo = score.Combo;
        lastMultiplier = score.Multiplier;
    }

    void OnComboBroken()
    {
        comboText.text = "Combo lost";
        comboText.color = comboBrokenColor;
        Punch(comboText.transform);
    }

    void Punch(Transform t) => t.localScale = Vector3.one * punchScale;

    void Update()
    {
        if (score == null) return;
        float dt = Time.unscaledDeltaTime;

        // roll the number up toward the real score
        shownScore = Mathf.Lerp(shownScore, score.Score, 1f - Mathf.Exp(-countUpSharpness * dt));
        if (Mathf.Abs(score.Score - shownScore) < 1f) shownScore = score.Score;
        int rounded = Mathf.RoundToInt(shownScore);
        if (rounded != shownScoreInt)
        {
            shownScoreInt = rounded;
            scoreText.text = rounded.ToString("N0");
        }

        float k = 1f - Mathf.Exp(-recoverSharpness * dt);
        comboText.transform.localScale = Vector3.Lerp(comboText.transform.localScale, Vector3.one, k);
        multiplierText.transform.localScale = Vector3.Lerp(multiplierText.transform.localScale, Vector3.one, k);
        comboText.color = Color.Lerp(comboText.color, comboColor, k * 0.3f);
    }
}
