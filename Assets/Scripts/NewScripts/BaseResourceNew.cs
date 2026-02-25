using UnityEngine;

public class BaseResourceNew : MonoBehaviour, IInteractableNew, IPIckableNew
{
    [SerializeField] private BaseResourceSO BaseResourceSO;
    public void Interact(Transform interactor)
    {
        Debug.Log("Interacted with Base Resource");
        PickedUp(interactor);
    }

    public void PickedUp(Transform parent)
    {
        parent.GetComponent<PlayerInteractionNew>().PickUpObject(this.gameObject);
    }

    private void Awake()
    {

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
