namespace Own.Blockchain.Public.Data

type DbChange = {
    Number : int
    Script : string
}

module DbChanges =

    let internal firebirdChanges : DbChange list =
        [
            {
                Number = 1
                Script =
                    """
                    CREATE TABLE db_version (
                        version_number INTEGER NOT NULL,
                        execution_timestamp BIGINT NOT NULL,

                        CONSTRAINT db_version__pk PRIMARY KEY (version_number)
                    );
                    """
            }
            {
                Number = 2
                Script =
                    """
                    CREATE TABLE tx (
                        tx_id BIGINT GENERATED BY DEFAULT AS IDENTITY NOT NULL,
                        tx_hash VARCHAR(50) NOT NULL,
                        sender_address VARCHAR(50) NOT NULL,
                        nonce BIGINT NOT NULL,
                        action_fee DECIMAL(18, 7) NOT NULL,
                        action_count SMALLINT NOT NULL,

                        CONSTRAINT tx__pk PRIMARY KEY (tx_id),
                        CONSTRAINT tx__uk__tx_hash UNIQUE (tx_hash)
                    );
                    CREATE INDEX tx__ix__sender_address ON tx (sender_address);
                    CREATE DESCENDING INDEX tx__ix__action_fee ON tx (action_fee);

                    CREATE TABLE block (
                        block_id BIGINT GENERATED BY DEFAULT AS IDENTITY NOT NULL,
                        block_number BIGINT NOT NULL,
                        block_hash VARCHAR(50) NOT NULL,
                        block_timestamp BIGINT NOT NULL,
                        is_config_block BOOLEAN NOT NULL,
                        is_applied BOOLEAN NOT NULL,

                        CONSTRAINT block__pk PRIMARY KEY (block_id),
                        CONSTRAINT block__uk__number UNIQUE (block_number),
                        CONSTRAINT block__uk__hash UNIQUE (block_hash),
                        CONSTRAINT block__uk__timestamp UNIQUE (block_timestamp)
                    );

                    CREATE TABLE chx_address (
                        chx_address_id BIGINT GENERATED BY DEFAULT AS IDENTITY NOT NULL,
                        blockchain_address VARCHAR(50) NOT NULL,
                        nonce BIGINT NOT NULL,
                        balance DECIMAL(18, 7) NOT NULL,

                        CONSTRAINT chx_address__pk PRIMARY KEY (chx_address_id),
                        CONSTRAINT chx_address__uk__address UNIQUE (blockchain_address)
                    );

                    CREATE TABLE account (
                        account_id BIGINT GENERATED BY DEFAULT AS IDENTITY NOT NULL,
                        account_hash VARCHAR(50) NOT NULL,
                        controller_address VARCHAR(50) NOT NULL,

                        CONSTRAINT account__pk PRIMARY KEY (account_id),
                        CONSTRAINT account__uk__account_hash UNIQUE (account_hash)
                    );

                    CREATE TABLE asset (
                        asset_id BIGINT GENERATED BY DEFAULT AS IDENTITY NOT NULL,
                        asset_hash VARCHAR(50) NOT NULL,
                        asset_code VARCHAR(20),
                        controller_address VARCHAR(50) NOT NULL,

                        CONSTRAINT asset__pk PRIMARY KEY (asset_id),
                        CONSTRAINT asset__uk__asset_hash UNIQUE (asset_hash),
                        CONSTRAINT asset__uk__asset_code UNIQUE (asset_code)
                    );

                    CREATE TABLE holding (
                        holding_id BIGINT GENERATED BY DEFAULT AS IDENTITY NOT NULL,
                        account_id BIGINT NOT NULL,
                        asset_hash VARCHAR(50) NOT NULL,
                        balance DECIMAL(18, 7) NOT NULL,

                        CONSTRAINT holding__pk PRIMARY KEY (holding_id),
                        CONSTRAINT holding__uk__acc_id__ast_hash UNIQUE (account_id, asset_hash),
                        CONSTRAINT holding__fk__account FOREIGN KEY (account_id)
                            REFERENCES account (account_id)
                    );
                    """
            }
            {
                Number = 3
                Script =
                    """
                    CREATE TABLE validator (
                        validator_id BIGINT GENERATED BY DEFAULT AS IDENTITY NOT NULL,
                        validator_address VARCHAR(50) NOT NULL,
                        network_address VARCHAR(250) NOT NULL,
                        shared_reward_percent DECIMAL(5, 2) NOT NULL,

                        CONSTRAINT validator__pk PRIMARY KEY (validator_id),
                        CONSTRAINT validator__uk__val_addr UNIQUE (validator_address)
                    );

                    CREATE TABLE stake (
                        stake_id BIGINT GENERATED BY DEFAULT AS IDENTITY NOT NULL,
                        staker_address VARCHAR(50) NOT NULL,
                        validator_address VARCHAR(50) NOT NULL,
                        amount DECIMAL(18, 7) NOT NULL,

                        CONSTRAINT stake__pk PRIMARY KEY (stake_id),
                        CONSTRAINT stake__uk__staker__validator
                            UNIQUE (staker_address, validator_address)
                    );

                    CREATE TABLE peer (
                        peer_id BIGINT GENERATED BY DEFAULT AS IDENTITY NOT NULL,
                        network_address VARCHAR(250) NOT NULL,

                        CONSTRAINT peer__pk PRIMARY KEY (peer_id),
                        CONSTRAINT peer__uk__network_address UNIQUE (network_address)
                    );
                    """
            }
            {
                Number = 4
                Script =
                    """
                    CREATE TABLE vote (
                        vote_id BIGINT GENERATED BY DEFAULT AS IDENTITY NOT NULL,
                        holding_id BIGINT NOT NULL,
                        resolution_hash VARCHAR(50) NOT NULL,
                        vote_hash VARCHAR(50) NOT NULL,
                        vote_weight DECIMAL(18, 7),

                        CONSTRAINT vote__pk PRIMARY KEY (vote_id),
                        CONSTRAINT vote__uk__holding__resolution UNIQUE (holding_id, resolution_hash),
                        CONSTRAINT vote__fk__holding FOREIGN KEY (holding_id)
                            REFERENCES holding (holding_id)
                    );

                    CREATE TABLE kyc_provider (
                        kyc_provider_id BIGINT GENERATED BY DEFAULT AS IDENTITY NOT NULL,
                        asset_id BIGINT NOT NULL,
                        provider_address VARCHAR(50) NOT NULL,

                        CONSTRAINT kyc_provider__pk PRIMARY KEY (kyc_provider_id),
                        CONSTRAINT kyc_provider__uk__asset__prov UNIQUE (asset_id, provider_address),
                        CONSTRAINT kyc_provider__fk__asset FOREIGN KEY (asset_id)
                            REFERENCES asset (asset_id)
                    );

                    CREATE TABLE eligibility (
                        eligibility_id BIGINT GENERATED BY DEFAULT AS IDENTITY NOT NULL,
                        account_id BIGINT NOT NULL,
                        asset_id BIGINT NOT NULL,
                        is_primary_eligible BOOLEAN NOT NULL,
                        is_secondary_eligible BOOLEAN NOT NULL,
                        kyc_controller_address VARCHAR(50) NOT NULL,

                        CONSTRAINT eligibility__pk PRIMARY KEY (eligibility_id),
                        CONSTRAINT eligibility__uk__account__asset UNIQUE (account_id, asset_id),
                        CONSTRAINT eligibility__fk__account FOREIGN KEY (account_id)
                            REFERENCES account (account_id),
                        CONSTRAINT eligibility__fk__asset FOREIGN KEY (asset_id)
                            REFERENCES asset (asset_id)
                    );
                    CREATE INDEX eligibility__ix__asset_id ON eligibility (asset_id);

                    ALTER TABLE asset ADD is_eligibility_required BOOLEAN DEFAULT FALSE NOT NULL;
                    ALTER TABLE asset ALTER is_eligibility_required DROP DEFAULT;

                    ALTER TABLE holding ADD is_emission BOOLEAN DEFAULT FALSE NOT NULL;
                    ALTER TABLE holding ALTER is_emission DROP DEFAULT;
                    """
            }
            {
                Number = 5
                Script =
                    """
                    CREATE TABLE equivocation (
                        equivocation_id BIGINT GENERATED BY DEFAULT AS IDENTITY NOT NULL,
                        equivocation_proof_hash VARCHAR(50) NOT NULL,
                        validator_address VARCHAR(50) NOT NULL,
                        block_number BIGINT NOT NULL,
                        consensus_round INT NOT NULL,
                        consensus_step SMALLINT NOT NULL,

                        CONSTRAINT equivocation__pk PRIMARY KEY (equivocation_id),
                        CONSTRAINT equivocation__uk__proof_hash UNIQUE (equivocation_proof_hash)
                    );
                    CREATE INDEX equivocation__ix__validator ON equivocation (validator_address);
                    CREATE INDEX equivocation__ix__b__r__s
                        ON equivocation (block_number, consensus_round, consensus_step);

                    ALTER TABLE validator ADD time_to_lock_deposit SMALLINT DEFAULT 0 NOT NULL;
                    ALTER TABLE validator ALTER time_to_lock_deposit DROP DEFAULT;

                    ALTER TABLE validator ADD time_to_blacklist SMALLINT DEFAULT 0 NOT NULL;
                    ALTER TABLE validator ALTER time_to_blacklist DROP DEFAULT;

                    ALTER TABLE validator ADD is_enabled BOOLEAN DEFAULT TRUE NOT NULL;
                    ALTER TABLE validator ALTER is_enabled DROP DEFAULT;
                    """
            }
            {
                Number = 6
                Script =
                    """
                    CREATE TABLE consensus_message (
                        consensus_message_id BIGINT GENERATED BY DEFAULT AS IDENTITY NOT NULL,
                        block_number BIGINT NOT NULL,
                        consensus_round INT NOT NULL,
                        consensus_step SMALLINT NOT NULL,
                        message_envelope BLOB SUB_TYPE TEXT NOT NULL,

                        CONSTRAINT consensus_message__pk PRIMARY KEY (consensus_message_id),
                        CONSTRAINT consensus_message__uk__b__r__s UNIQUE (block_number, consensus_round, consensus_step)
                    );

                    CREATE TABLE consensus_state (
                        consensus_state_id SMALLINT NOT NULL,
                        block_number BIGINT NOT NULL,
                        consensus_round INT NOT NULL,
                        consensus_step SMALLINT NOT NULL,
                        locked_block BLOB SUB_TYPE TEXT,
                        locked_round INT NOT NULL,
                        valid_block BLOB SUB_TYPE TEXT,
                        valid_round INT NOT NULL,
                        valid_block_signatures BLOB SUB_TYPE TEXT,

                        CONSTRAINT consensus_state__pk PRIMARY KEY (consensus_state_id),
                        CONSTRAINT consensus_state__ck__id__is_0 CHECK (consensus_state_id = 0) -- Single row table
                    );
                    """
            }
            {
                Number = 7
                Script =
                    """
                    ALTER TABLE peer ADD session_timestamp BIGINT DEFAULT 0 NOT NULL;
                    ALTER TABLE peer ALTER session_timestamp DROP DEFAULT;

                    ALTER TABLE peer ADD is_dead BOOLEAN DEFAULT FALSE NOT NULL;
                    ALTER TABLE peer ALTER is_dead DROP DEFAULT;

                    ALTER TABLE peer ADD dead_timestamp BIGINT;
                    """
            }
        ]

    let internal postgresChanges : DbChange list =
        [
            {
                Number = 1
                Script =
                    """
                    CREATE TABLE db_version (
                        version_number INTEGER NOT NULL,
                        execution_timestamp BIGINT NOT NULL,

                        CONSTRAINT db_version__pk PRIMARY KEY (version_number)
                    );
                    """
            }
            {
                Number = 2
                Script =
                    """
                    CREATE TABLE tx (
                        tx_id BIGSERIAL NOT NULL,
                        tx_hash VARCHAR(50) NOT NULL,
                        sender_address VARCHAR(50) NOT NULL,
                        nonce BIGINT NOT NULL,
                        action_fee DECIMAL(18, 7) NOT NULL,
                        action_count SMALLINT NOT NULL,

                        CONSTRAINT tx__pk PRIMARY KEY (tx_id),
                        CONSTRAINT tx__uk__tx_hash UNIQUE (tx_hash)
                    );
                    CREATE INDEX tx__ix__sender_address ON tx (sender_address);
                    CREATE INDEX tx__ix__action_fee ON tx (action_fee DESC);

                    CREATE TABLE block (
                        block_id BIGSERIAL NOT NULL,
                        block_number BIGINT NOT NULL,
                        block_hash VARCHAR(50) NOT NULL,
                        block_timestamp BIGINT NOT NULL,
                        is_config_block BOOLEAN NOT NULL,
                        is_applied BOOLEAN NOT NULL,

                        CONSTRAINT block__pk PRIMARY KEY (block_id),
                        CONSTRAINT block__uk__number UNIQUE (block_number),
                        CONSTRAINT block__uk__hash UNIQUE (block_hash),
                        CONSTRAINT block__uk__timestamp UNIQUE (block_timestamp)
                    );

                    CREATE TABLE chx_address (
                        chx_address_id BIGSERIAL NOT NULL,
                        blockchain_address VARCHAR(50) NOT NULL,
                        nonce BIGINT NOT NULL,
                        balance DECIMAL(18, 7) NOT NULL,

                        CONSTRAINT chx_address__pk PRIMARY KEY (chx_address_id),
                        CONSTRAINT chx_address__uk__blockchain_address UNIQUE (blockchain_address)
                    );

                    CREATE TABLE account (
                        account_id BIGSERIAL NOT NULL,
                        account_hash VARCHAR(50) NOT NULL,
                        controller_address VARCHAR(50) NOT NULL,

                        CONSTRAINT account__pk PRIMARY KEY (account_id),
                        CONSTRAINT account__uk__account_hash UNIQUE (account_hash)
                    );

                    CREATE TABLE asset (
                        asset_id BIGSERIAL NOT NULL,
                        asset_hash VARCHAR(50) NOT NULL,
                        asset_code VARCHAR(20),
                        controller_address VARCHAR(50) NOT NULL,

                        CONSTRAINT asset__pk PRIMARY KEY (asset_id),
                        CONSTRAINT asset__uk__asset_hash UNIQUE (asset_hash),
                        CONSTRAINT asset__uk__asset_code UNIQUE (asset_code)
                    );

                    CREATE TABLE holding (
                        holding_id BIGSERIAL NOT NULL,
                        account_id BIGINT NOT NULL,
                        asset_hash VARCHAR(50) NOT NULL,
                        balance DECIMAL(18, 7) NOT NULL,

                        CONSTRAINT holding__pk PRIMARY KEY (holding_id),
                        CONSTRAINT holding__uk__account_id__asset_hash UNIQUE (account_id, asset_hash),
                        CONSTRAINT holding__fk__account FOREIGN KEY (account_id)
                            REFERENCES account (account_id)
                    );
                    """
            }
            {
                Number = 3
                Script =
                    """
                    CREATE TABLE validator (
                        validator_id BIGSERIAL NOT NULL,
                        validator_address VARCHAR(50) NOT NULL,
                        network_address VARCHAR(250) NOT NULL,
                        shared_reward_percent DECIMAL(5, 2) NOT NULL,

                        CONSTRAINT validator__pk PRIMARY KEY (validator_id),
                        CONSTRAINT validator__uk__validator_address UNIQUE (validator_address)
                    );

                    CREATE TABLE stake (
                        stake_id BIGSERIAL NOT NULL,
                        staker_address VARCHAR(50) NOT NULL,
                        validator_address VARCHAR(50) NOT NULL,
                        amount DECIMAL(18, 7) NOT NULL,

                        CONSTRAINT stake__pk PRIMARY KEY (stake_id),
                        CONSTRAINT stake__uk__staker_address__validator_address
                            UNIQUE (staker_address, validator_address)
                    );

                    CREATE TABLE peer (
                        peer_id BIGSERIAL NOT NULL,
                        network_address VARCHAR(250) NOT NULL,

                        CONSTRAINT peer__pk PRIMARY KEY (peer_id),
                        CONSTRAINT peer__uk__network_address UNIQUE (network_address)
                    );
                    """
            }
            {
                Number = 4
                Script =
                    """
                    CREATE TABLE vote (
                        vote_id BIGSERIAL NOT NULL,
                        holding_id BIGINT NOT NULL,
                        resolution_hash VARCHAR(50) NOT NULL,
                        vote_hash VARCHAR(50) NOT NULL,
                        vote_weight DECIMAL(18, 7),

                        CONSTRAINT vote__pk PRIMARY KEY (vote_id),
                        CONSTRAINT vote__uk__holding_id__resolution_hash UNIQUE (holding_id, resolution_hash),
                        CONSTRAINT vote__fk__holding FOREIGN KEY (holding_id)
                            REFERENCES holding (holding_id)
                    );

                    CREATE TABLE kyc_provider (
                        kyc_provider_id BIGSERIAL NOT NULL,
                        asset_id BIGINT NOT NULL,
                        provider_address VARCHAR(50) NOT NULL,

                        CONSTRAINT kyc_provider__pk PRIMARY KEY (kyc_provider_id),
                        CONSTRAINT kyc_provider__uk__asset__prov UNIQUE (asset_id, provider_address),
                        CONSTRAINT kyc_provider__fk__asset FOREIGN KEY (asset_id)
                            REFERENCES asset (asset_id)
                    );

                    CREATE TABLE eligibility (
                        eligibility_id BIGSERIAL NOT NULL,
                        account_id BIGINT NOT NULL,
                        asset_id BIGINT NOT NULL,
                        is_primary_eligible BOOLEAN NOT NULL,
                        is_secondary_eligible BOOLEAN NOT NULL,
                        kyc_controller_address VARCHAR(50) NOT NULL,

                        CONSTRAINT eligibility__pk PRIMARY KEY (eligibility_id),
                        CONSTRAINT eligibility__uk__account__asset UNIQUE (account_id, asset_id),
                        CONSTRAINT eligibility__fk__account FOREIGN KEY (account_id)
                            REFERENCES account (account_id),
                        CONSTRAINT eligibility__fk__asset FOREIGN KEY (asset_id)
                            REFERENCES asset (asset_id)
                    );
                    CREATE INDEX eligibility__ix__asset_id ON eligibility (asset_id);

                    ALTER TABLE asset ADD is_eligibility_required BOOL DEFAULT FALSE NOT NULL;
                    ALTER TABLE asset ALTER is_eligibility_required DROP DEFAULT;

                    ALTER TABLE holding ADD is_emission BOOL DEFAULT FALSE NOT NULL;
                    ALTER TABLE holding ALTER is_emission DROP DEFAULT;
                    """
            }
            {
                Number = 5
                Script =
                    """
                    CREATE TABLE equivocation (
                        equivocation_id BIGSERIAL NOT NULL,
                        equivocation_proof_hash VARCHAR(50) NOT NULL,
                        validator_address VARCHAR(50) NOT NULL,
                        block_number BIGINT NOT NULL,
                        consensus_round INT NOT NULL,
                        consensus_step SMALLINT NOT NULL,

                        CONSTRAINT equivocation__pk PRIMARY KEY (equivocation_id),
                        CONSTRAINT equivocation__uk__equivocation_proof_hash UNIQUE (equivocation_proof_hash)
                    );
                    CREATE INDEX equivocation__ix__validator_address ON equivocation (validator_address);
                    CREATE INDEX equivocation__ix__block_number__consensus_round__consensus_step
                        ON equivocation (block_number, consensus_round, consensus_step);

                    ALTER TABLE validator ADD time_to_lock_deposit SMALLINT DEFAULT 0 NOT NULL;
                    ALTER TABLE validator ALTER time_to_lock_deposit DROP DEFAULT;

                    ALTER TABLE validator ADD time_to_blacklist SMALLINT DEFAULT 0 NOT NULL;
                    ALTER TABLE validator ALTER time_to_blacklist DROP DEFAULT;

                    ALTER TABLE validator ADD is_enabled BOOLEAN DEFAULT TRUE NOT NULL;
                    ALTER TABLE validator ALTER is_enabled DROP DEFAULT;
                    """
            }
            {
                Number = 6
                Script =
                    """
                    CREATE TABLE consensus_message (
                        consensus_message_id BIGSERIAL NOT NULL,
                        block_number BIGINT NOT NULL,
                        consensus_round INT NOT NULL,
                        consensus_step SMALLINT NOT NULL,
                        message_envelope TEXT NOT NULL,

                        CONSTRAINT consensus_message__pk PRIMARY KEY (consensus_message_id),
                        CONSTRAINT consensus_message__uk__block_number__round__step
                            UNIQUE (block_number, consensus_round, consensus_step)
                    );

                    CREATE TABLE consensus_state (
                        consensus_state_id SMALLINT NOT NULL,
                        block_number BIGINT NOT NULL,
                        consensus_round INT NOT NULL,
                        consensus_step SMALLINT NOT NULL,
                        locked_block TEXT,
                        locked_round INT NOT NULL,
                        valid_block TEXT,
                        valid_round INT NOT NULL,
                        valid_block_signatures TEXT,

                        CONSTRAINT consensus_state__pk PRIMARY KEY (consensus_state_id),
                        CONSTRAINT consensus_state__ck__id__is_0 CHECK (consensus_state_id = 0) -- Single row table
                    );
                    """
            }
            {
                Number = 7
                Script =
                    """
                    ALTER TABLE peer ADD session_timestamp BIGINT DEFAULT 0 NOT NULL;
                    ALTER TABLE peer ALTER session_timestamp DROP DEFAULT;

                    ALTER TABLE peer ADD is_dead BOOLEAN DEFAULT FALSE NOT NULL;
                    ALTER TABLE peer ALTER is_dead DROP DEFAULT;

                    ALTER TABLE peer ADD dead_timestamp BIGINT;
                    """
            }
        ]
