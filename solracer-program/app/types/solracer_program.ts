/**
 * Program IDL in camelCase format in order to be used in JS/TS.
 *
 * Note that this is only a type helper and is not the actual IDL. The original
 * IDL can be found at `target/idl/solracer_program.json`.
 */
export type SolracerProgram = {
  "address": "BW9EBdw58SZzzYY3rczk6qGeRUf21ZyPJyd6QKs4GbtM",
  "metadata": {
    "name": "solracerProgram",
    "version": "0.1.0",
    "spec": "0.1.0",
    "description": "Created with Anchor"
  },
  "instructions": [
    {
      "name": "claimPrize",
      "discriminator": [
        157,
        233,
        139,
        121,
        246,
        62,
        234,
        235
      ],
      "accounts": [
        {
          "name": "race",
          "writable": true
        },
        {
          "name": "winner",
          "writable": true,
          "signer": true
        }
      ],
      "args": []
    },
    {
      "name": "createRace",
      "discriminator": [
        233,
        107,
        148,
        159,
        241,
        155,
        226,
        54
      ],
      "accounts": [
        {
          "name": "race",
          "writable": true,
          "pda": {
            "seeds": [
              {
                "kind": "const",
                "value": [
                  114,
                  97,
                  99,
                  101
                ]
              },
              {
                "kind": "arg",
                "path": "raceId"
              },
              {
                "kind": "arg",
                "path": "tokenMint"
              },
              {
                "kind": "arg",
                "path": "entryFeeSol"
              }
            ]
          }
        },
        {
          "name": "player1",
          "writable": true,
          "signer": true
        },
        {
          "name": "systemProgram",
          "address": "11111111111111111111111111111111"
        }
      ],
      "args": [
        {
          "name": "raceId",
          "type": "string"
        },
        {
          "name": "tokenMint",
          "type": "pubkey"
        },
        {
          "name": "entryFeeSol",
          "type": "u64"
        }
      ]
    },
    {
      "name": "joinRace",
      "discriminator": [
        207,
        91,
        222,
        84,
        249,
        246,
        229,
        54
      ],
      "accounts": [
        {
          "name": "race",
          "writable": true
        },
        {
          "name": "player2",
          "writable": true,
          "signer": true
        },
        {
          "name": "systemProgram",
          "address": "11111111111111111111111111111111"
        }
      ],
      "args": []
    },
    {
      "name": "settleRace",
      "discriminator": [
        172,
        32,
        72,
        212,
        155,
        33,
        161,
        237
      ],
      "accounts": [
        {
          "name": "race",
          "writable": true
        }
      ],
      "args": []
    },
    {
      "name": "submitResult",
      "discriminator": [
        240,
        42,
        89,
        180,
        10,
        239,
        9,
        214
      ],
      "accounts": [
        {
          "name": "race",
          "writable": true
        },
        {
          "name": "player",
          "signer": true
        }
      ],
      "args": [
        {
          "name": "finishTimeMs",
          "type": "u64"
        },
        {
          "name": "coinsCollected",
          "type": "u64"
        },
        {
          "name": "inputHash",
          "type": {
            "array": [
              "u8",
              32
            ]
          }
        }
      ]
    }
  ],
  "accounts": [
    {
      "name": "race",
      "discriminator": [
        114,
        93,
        186,
        119,
        99,
        123,
        162,
        192
      ]
    }
  ],
  "errors": [
    {
      "code": 6000,
      "name": "invalidRaceStatus",
      "msg": "Invalid race status for this operation"
    },
    {
      "code": 6001,
      "name": "player2AlreadySet",
      "msg": "Player2 is already set"
    },
    {
      "code": 6002,
      "name": "playerNotInRace",
      "msg": "Player is not in this race"
    },
    {
      "code": 6003,
      "name": "resultAlreadySubmitted",
      "msg": "Result already submitted for this player"
    },
    {
      "code": 6004,
      "name": "resultsNotComplete",
      "msg": "Both results must be submitted before settling"
    },
    {
      "code": 6005,
      "name": "notWinner",
      "msg": "Only the winner can claim the prize"
    }
  ],
  "types": [
    {
      "name": "race",
      "type": {
        "kind": "struct",
        "fields": [
          {
            "name": "raceId",
            "type": "string"
          },
          {
            "name": "tokenMint",
            "type": "pubkey"
          },
          {
            "name": "entryFeeSol",
            "type": "u64"
          },
          {
            "name": "player1",
            "type": "pubkey"
          },
          {
            "name": "player2",
            "type": {
              "option": "pubkey"
            }
          },
          {
            "name": "status",
            "type": {
              "defined": {
                "name": "raceStatus"
              }
            }
          },
          {
            "name": "player1Result",
            "type": {
              "option": {
                "defined": {
                  "name": "raceResult"
                }
              }
            }
          },
          {
            "name": "player2Result",
            "type": {
              "option": {
                "defined": {
                  "name": "raceResult"
                }
              }
            }
          },
          {
            "name": "winner",
            "type": {
              "option": "pubkey"
            }
          },
          {
            "name": "escrowAmount",
            "type": "u64"
          },
          {
            "name": "createdAt",
            "type": "i64"
          },
          {
            "name": "bump",
            "type": "u8"
          }
        ]
      }
    },
    {
      "name": "raceResult",
      "type": {
        "kind": "struct",
        "fields": [
          {
            "name": "finishTimeMs",
            "type": "u64"
          },
          {
            "name": "coinsCollected",
            "type": "u64"
          },
          {
            "name": "inputHash",
            "type": {
              "array": [
                "u8",
                32
              ]
            }
          }
        ]
      }
    },
    {
      "name": "raceStatus",
      "type": {
        "kind": "enum",
        "variants": [
          {
            "name": "waiting"
          },
          {
            "name": "active"
          },
          {
            "name": "settled"
          }
        ]
      }
    }
  ]
};
