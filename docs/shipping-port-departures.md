# Shipping port free departures

PraetorisClient can give one More World Locations AIO shipping port free teleport departures. Trips to that port retain the normal MWL price. Shipment prices, destination discovery, and teleport item restrictions remain unchanged.

Install the candidate PraetorisClient on the server and every participating client. MWL is an optional dependency. If MWL is absent or its teleport method is incompatible, this feature does not apply.

## Select a port

As a server administrator, open the port panel while standing beside its trader. Then run:

```text
shippingport_free guid
shippingport_free set
shippingport_free status
```

`guid` prints the current port's saved `PortGUID`. `set` asks the server to select this port. The server checks administrator access and reads the GUID from the port's network record. The server saves the setting and synchronizes it to clients. Selecting another port replaces the previous selection.

To disable free departures:

```text
shippingport_free clear
```

The server setting in `warpalicious.PraetorisClient.cfg` is:

```ini
[ShippingPorts]
FreeDeparturePortGuid =
```

Empty disables the feature. A valid GUID selects exactly one saved port. Renaming a port preserves its selection. Destroying and replacing its trader creates a new GUID, so select the replacement again. The configured GUID must belong to the current world.

MWL checks and consumes travel currency on the client. This feature uses that existing payment model. It does not add server validation of teleport payments.

## Validation in the current Season 8 test profiles

Run the local checks with `dotnet run --project Tests/ShippingPorts/ShippingPorts.csproj -c Release`. These checks link the feature source and simulate game records and patch application. They cover cost direction, payment consumption, GUID selection, saved writes, and request permissions. They do not prove Harmony application inside Valheim or multiplayer configuration synchronization.

- Record server and client profile and modpack versions.
- Select port A. With no coins and no cost cheat, verify A to a known port B succeeds and displays zero cost.
- Verify B to A displays the normal price, rejects insufficient funds, and consumes the correct amount when funded.
- Verify B to another normal port retains its normal price.
- Verify forbidden teleport items still prevent departure from A.
- Rename A. Verify it still gives free departures.
- Reconnect and restart the test server. Verify the saved selection still applies.
- Verify a second client receives the selection and has the same behavior.
- Verify a non-administrator cannot change the selection through the request handler.
- Select B. Verify A resumes charging. Clear the setting and verify B resumes charging.
- Restore the original test profile files and remove candidate additions.

Live multiplayer validation is required before production deployment.
