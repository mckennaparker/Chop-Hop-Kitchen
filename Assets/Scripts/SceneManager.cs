using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class SceneManager : MonoBehaviour
{
    private readonly InputAction _aButtonAction = new InputAction(
        "A Button",
        InputActionType.Button,
        "<XRController>{RightHand}/primaryButton");
    private readonly InputAction _bButtonAction = new InputAction(
        "B Button",
        InputActionType.Button,
        "<XRController>{RightHand}/secondaryButton");

    private void OnEnable()
    {
        _aButtonAction.performed += LoadSceneOne;
        _bButtonAction.performed += LoadSceneZero;
        _aButtonAction.Enable();
        _bButtonAction.Enable();
    }

    private void OnDisable()
    {
        _aButtonAction.performed -= LoadSceneOne;
        _bButtonAction.performed -= LoadSceneZero;
        _aButtonAction.Disable();
        _bButtonAction.Disable();
    }

    private void OnDestroy()
    {
        _aButtonAction.Dispose();
        _bButtonAction.Dispose();
    }

    private void LoadSceneOne(InputAction.CallbackContext context)
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex == 0)
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(1);
        }
    }

    private void LoadSceneZero(InputAction.CallbackContext context)
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex == 1)
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(0);
        }
    }
}
