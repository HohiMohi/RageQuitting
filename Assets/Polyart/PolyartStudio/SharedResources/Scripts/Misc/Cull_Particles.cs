using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.ParticleSystem;


namespace Polyart
{
    public class Cull_Particles : MonoBehaviour
    {
        public GameObject particle;
        public bool isEnabled = false;
        // Start is called before the first frame update
        void Start()
        {
            if (isEnabled)
            {
                particle.SetActive(true);
            }
            else
            {
                particle.SetActive(false);
            }

        }

        // Update is called once per frame
        void Update()
        {

        }
    }
}
