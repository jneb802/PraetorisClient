using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PraetorisClient.BodyBounties;

public sealed class TombstoneBounty : MonoBehaviour
{
    private const string LockRpc = "Praetoris_BountyLock_v1";
    private const string UnlockRpc = "Praetoris_BountyUnlock_v1";
    private static readonly HashSet<TombstoneBounty> Loaded = new();
    private ZNetView _view = null!;
    private Container _container = null!;
    internal TombStone Stone = null!;
    private GameObject? _glow;
    internal bool Locked;
    internal bool Ready;
    private string _lockBounty = "";

    private void Awake()
    {
        _view = GetComponent<ZNetView>();
        _container = GetComponent<Container>();
        Stone = GetComponent<TombStone>();
        if (_view == null || !_view.IsValid()) return;
        _view.Register<string, string>(LockRpc, OnLock);
        _view.Register<bool>(UnlockRpc, OnUnlock);
        // FindHoverObject prefers a Hoverable directly on the hit collider.
        // A child with exactly one Interactable guarantees dispatch, regardless
        // of component ordering on the original TombStone GameObject.
        foreach (Collider collider in GetComponentsInChildren<Collider>(true))
        {
            GameObject target = collider.gameObject;
            if (target == gameObject)
            {
                target = new GameObject("BountyInteraction");
                target.layer = gameObject.layer;
                target.transform.SetParent(transform, false);
                Collider copy;
                if (collider is BoxCollider box)
                { BoxCollider value = target.AddComponent<BoxCollider>(); value.center = box.center; value.size = box.size; copy = value; }
                else if (collider is CapsuleCollider capsule)
                { CapsuleCollider value = target.AddComponent<CapsuleCollider>(); value.center = capsule.center; value.radius = capsule.radius; value.height = capsule.height; value.direction = capsule.direction; copy = value; }
                else if (collider is SphereCollider sphere)
                { SphereCollider value = target.AddComponent<SphereCollider>(); value.center = sphere.center; value.radius = sphere.radius; copy = value; }
                else if (collider is MeshCollider mesh)
                { MeshCollider value = target.AddComponent<MeshCollider>(); value.sharedMesh = mesh.sharedMesh; value.convex = mesh.convex; copy = value; }
                else { Destroy(target); throw new InvalidOperationException("Unsupported tombstone collider: " + collider.GetType().Name); }
                copy.sharedMaterial = collider.sharedMaterial;
                copy.isTrigger = collider.isTrigger;
                copy.enabled = collider.enabled;
                collider.enabled = false;
                Destroy(collider);
            }
            if (target.GetComponent<TombstoneBountyInteraction>() == null)
                target.AddComponent<TombstoneBountyInteraction>().Bounty = this;
        }
    }

    private void Start()
    {
        if (_view == null || !_view.IsValid()) return;
        Loaded.Add(this);
        Ready = false;
        Refresh();
        _container.GetInventory().m_onChanged += OnInventoryChanged;
        if (ZNet.instance.IsServer())
        {
            BountyRecord? record = BountyService.ServerRecord(_view.GetZDO().m_uid.ToString());
            Ready = true;
            Locked = record != null && (record.State == BountyState.Collecting || record.State == BountyState.InTransit ||
                record.State == BountyState.Delivered || record.State == BountyState.Reclaimed);
            if (Locked && record!.State != BountyState.Collecting && _view.IsOwner())
            { _container.GetInventory().RemoveAll(); _container.Save(); }
        }
        else BountyService.Request(new BountyMessage { Action = "stoneLoaded", Stone = _view.GetZDO().m_uid.ToString() });
    }

    private void OnInventoryChanged()
    {
        if (Locked || Player.m_localPlayer == null || !_view.IsValid() || _container.GetInventory().NrOfItems() != 0) return;
        if (_view.GetZDO().GetLong(ZDOVars.s_owner) != Player.m_localPlayer.GetPlayerID()) return;
        BountyService.Request(new BountyMessage { Action = "ownerRecovered", Stone = _view.GetZDO().m_uid.ToString() });
    }

    internal BountyRecord? Record => _view == null || !_view.IsValid() ? null :
        BountyService.ClientRecords.Values.FirstOrDefault(bounty => bounty.Stone == _view.GetZDO().m_uid.ToString() &&
            bounty.State != BountyState.Cancelled);

    private void Refresh()
    {
        BountyRecord? record = Record;
        bool active = record != null && (record.State == BountyState.Posted || record.State == BountyState.Accepted);
        if (active && _glow == null)
        {
            _glow = new GameObject("BountyLight");
            _glow.transform.SetParent(transform, false);
            _glow.transform.localPosition = Vector3.up;
            Light light = _glow.AddComponent<Light>();
            light.color = new Color(1f, 0.65f, 0.15f); light.range = 3f; light.intensity = 1.5f;
        }
        if (_glow != null) _glow.SetActive(active);
        Locked = _lockBounty != "" || record != null && (record.State == BountyState.Collecting ||
            record.State == BountyState.InTransit || record.State == BountyState.Delivered || record.State == BountyState.Reclaimed);
    }

    internal static void RefreshLoaded()
    {
        foreach (TombstoneBounty bounty in Loaded) if (bounty != null) bounty.Refresh();
    }

    internal static void OnPlayerJoined()
    {
        foreach (TombstoneBounty bounty in Loaded)
            if (bounty != null && bounty._view.IsValid())
                BountyService.Request(new BountyMessage { Action = "stoneLoaded", Stone = bounty._view.GetZDO().m_uid.ToString() });
    }

    internal static bool Blocks(Container container)
    {
        TombstoneBounty component = container.GetComponent<TombstoneBounty>();
        return component != null && (!component.Ready || component.Locked);
    }

    internal static void ApplyStatus(string stone, bool locked, bool consumed)
    {
        foreach (TombstoneBounty bounty in Loaded)
        {
            if (bounty == null || !bounty._view.IsValid() || bounty._view.GetZDO().m_uid.ToString() != stone) continue;
            bounty.Locked = locked || consumed;
            bounty.Ready = true;
            if (consumed && bounty._view.IsOwner())
            {
                bounty._container.GetInventory().RemoveAll();
                bounty._container.Save();
            }
            bounty.Refresh();
        }
    }

    internal static void RequestLock(ZDO stone, string bounty, string fingerprint) =>
        ZRoutedRpc.instance.InvokeRoutedRPC(stone.GetOwner(), stone.m_uid, LockRpc, bounty, fingerprint);

    internal static void ReleaseLock(ZDO stone, bool consume) =>
        ZRoutedRpc.instance.InvokeRoutedRPC(stone.GetOwner(), stone.m_uid, UnlockRpc, consume);

    private void OnLock(long sender, string bounty, string fingerprint)
    {
        if (!BountyService.FromServer(sender) || !_view.IsOwner()) return;
        string contents = BountyInventory.Save(_container.GetInventory());
        bool valid = !_container.IsInUse() && BountyInventory.Hash(contents) == fingerprint && (!Locked || _lockBounty == bounty || _lockBounty == "");
        if (valid) { Locked = true; _lockBounty = bounty; }
        BountyService.Request(new BountyMessage { Action = "stoneReady", Id = bounty, Success = valid, Fingerprint = BountyInventory.Hash(contents) });
    }

    private void OnUnlock(long sender, bool consume)
    {
        if (!BountyService.FromServer(sender) || !_view.IsOwner()) return;
        if (consume)
        {
            _container.GetInventory().RemoveAll();
            _container.Save();
        }
        Locked = false; _lockBounty = "";
    }

    private void OnDestroy()
    {
        Loaded.Remove(this);
        if (_container != null) _container.GetInventory().m_onChanged -= OnInventoryChanged;
    }
}

public sealed class TombstoneBountyInteraction : MonoBehaviour, Interactable, Hoverable
{
    public float GetHoverOffset() => Bounty.Stone.GetHoverOffset();
    internal TombstoneBounty Bounty = null!;
    public string GetHoverName() => Bounty.Stone.GetHoverName();
    public string GetHoverText()
    {
        BountyRecord? record = Bounty.Record;
        if (!Bounty.Ready || Bounty.Locked) return "Body retrieval transaction pending";
        if (record == null || Player.m_localPlayer == null || record.Owner == Player.m_localPlayer.GetPlayerID()) return Bounty.Stone.GetHoverText();
        return Localization.instance.Localize("<color=#EBC66B>Body retrieval bounty</color>\n" +
            System.Security.SecurityElement.Escape(record.OwnerName) + " — " + record.Reward + " Coins\n[$KEY_Use] Inspect bounty");
    }
    public bool Interact(Humanoid user, bool hold, bool alt)
    {
        if (hold || Bounty.Locked) return false;
        if (!Bounty.Ready)
        {
            BountyService.Request(new BountyMessage { Action = "stoneLoaded", Stone = Bounty.GetComponent<ZNetView>().GetZDO().m_uid.ToString() });
            return true;
        }
        BountyRecord? record = Bounty.Record;
        if (record == null || user is Player player && record.Owner == player.GetPlayerID()) return Bounty.Stone.Interact(user, false, alt);
        BountyService.Request(new BountyMessage { Action = "inspect", Id = record.Id });
        return true;
    }
    public bool UseItem(Humanoid user, ItemDrop.ItemData item) => !Bounty.Locked && Bounty.Stone.UseItem(user, item);
}
