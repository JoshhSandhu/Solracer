/// Migration placeholder — no on-chain state migration needed for ER program.
/// The ER program only creates/updates transient position PDAs.
const anchor = require("@coral-xyz/anchor");

module.exports = async function (provider) {
    anchor.setProvider(provider);
    console.log("No migration needed for solracer-er");
};
