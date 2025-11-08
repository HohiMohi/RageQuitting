using Polyart;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace Polyart
{
    public class SceneEntrance : MonoBehaviour
    {
        public string lastExitName;
        // Start is called before the first frame update
        void Start()
        {
            if (PlayerPrefs.GetString("LastExitName") == lastExitName)
            {
                FirstPersonController_Polyart.instance.transform.position = transform.position;
                FirstPersonController_Polyart.instance.transform.eulerAngles = transform.eulerAngles;
            }
        }

        // Update is called once per frame
        void Update()
        {

        }
    }
}
