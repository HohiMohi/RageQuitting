using UnityEngine;

public class BaseFactory : MonoBehaviour, IInteractableNew
{
    [Header("Temp Value")]
    public GameObject InteractionOutlineGameobject;
    
    public void Interact(Transform interactor)
    {
       Debug.Log("Interacted with Base Factory");
    }
    private void Awake()
    {
        InteractionOutlineGameobject.SetActive(false);
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
