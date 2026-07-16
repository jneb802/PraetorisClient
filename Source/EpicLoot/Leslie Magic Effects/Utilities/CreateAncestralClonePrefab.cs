using EpicLoot.Abilities;
using EpicLoot.Adventure;
using UnityEngine;


namespace EpicLootLeslieAlphaTest.src.Utilities;
public class HumanoidFactory
{
    public static GameObject playerAncestor;
    private static bool Loaded = false;
    public static void Create()
    {
        if (Loaded) return;
        GameObject prefab = Game.instance.m_playerPrefab;
        bool prefabActive = prefab.activeSelf; // save stae of prefab to restore - for safety


        prefab.SetActive(false); // prevent awake stuff and subsequent other runs 
        Player playerRefForCopy = prefab.GetComponent<Player>();
        playerAncestor = Object.Instantiate(prefab);

        // "playerAncestor GameObject stripped of Player Components"
        playerAncestor.Remove<AbilityController>();
        playerAncestor.Remove<AdventureComponent>();
        playerAncestor.Remove<Player>();
        playerAncestor.Remove<PlayerController>();
        playerAncestor.Remove<Talker>();
        playerAncestor.Remove<Skills>();
        playerAncestor.Remove<ZSyncTransform>();
        playerAncestor.name = "playerAncestorHashString";

        playerAncestor.GetComponent<ZNetView>().m_persistent = false;
        playerAncestor.GetComponent<ZNetView>().m_type = ZDO.ObjectType.Default;



        // make the fucking universe

        Humanoid ancestral_Clone = playerAncestor.AddComponent<Humanoid>();
        ancestral_Clone.CopyFieldsFrom(playerRefForCopy);
        ancestral_Clone.m_animator = playerAncestor.GetComponentInChildren<Animator>();
        ancestral_Clone.m_zanim = playerAncestor.GetComponent<ZSyncAnimation>();
        ancestral_Clone.m_body = playerAncestor.GetComponent<Rigidbody>();
        ancestral_Clone.m_collider = playerAncestor.GetComponent<CapsuleCollider>();
        ancestral_Clone.m_visEquipment = playerAncestor.GetComponent<VisEquipment>();
        ancestral_Clone.m_turnSpeed = 0f;
        ancestral_Clone.m_animEvent = playerAncestor.GetComponentInChildren<CharacterAnimEvent>();
        ancestral_Clone.m_eye = Utils.FindChild(playerAncestor.transform, "EyePos");


        if (playerAncestor.GetComponent<ZSyncAnimation>() != null)
        {
            playerAncestor.GetComponent<ZSyncAnimation>().m_animator = playerAncestor.GetComponent<Animator>();
            playerAncestor.GetComponent<ZSyncAnimation>().m_nview = playerAncestor.GetComponent<ZNetView>();
        }

        Rigidbody rb = playerAncestor.GetComponent<Rigidbody>(); // allow clone to be phased through and not fall through the ground
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = false;
            rb.constraints = RigidbodyConstraints.FreezeAll;
        }

        Shader ghostShader = Shader.Find("Sprites/Default");
        foreach (Renderer rend in playerAncestor.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = rend.materials;
            //for (int i = 0; i < mats.Length; i++)
            //{
            //    mats[i].shader = ghostShader;
            //    mats[i].mainTexture = null;
            //    mats[i].color = new Color(0.5f, 0.7f, 1f, 0.02f);
            //}
            mats[0].shader = ghostShader;
            mats[0].mainTexture = null;
            mats[0].color = new Color(.3f, .5f, .9f, .03f);

            mats[1].shader = ghostShader;
            mats[1].mainTexture = null;
            mats[1].color = new Color(.3f, .5f, .9f, .0f);

            mats[0].SetColor("_SkinColor", new Color(0.3f, 0.5f, 0.9f, .03f));

            Shader s = mats[0].shader;
            rend.materials = mats;
        }

        foreach (Renderer rend in playerAncestor.GetComponentsInChildren<Renderer>(true))
        {
            string meshName = "";
            if (rend is SkinnedMeshRenderer smr && smr.sharedMesh != null)
            {
                meshName = smr.sharedMesh.name;
            }
            else if (rend is MeshRenderer mr && rend.GetComponent<MeshFilter>() is MeshFilter mf && mf.sharedMesh != null)
            {
                meshName = mf.sharedMesh.name;
            }
        }

        foreach (Collider col in playerAncestor.GetComponentsInChildren<Collider>()) col.enabled = false;

        ZNetScene.instance.m_namedPrefabs["playerAncestorHashString".GetStableHashCode()] = playerAncestor;
        prefab.SetActive(prefabActive);
        playerAncestor.SetActive(false);
        Loaded = true;
    }
}

