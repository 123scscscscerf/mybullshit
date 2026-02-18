<?php

declare(strict_types=1);

return [
    'app_name' => 'the.sytes.net (v2 – file server edition)',
    'db_path' => __DIR__ . '/../data/database.sqlite',
    'max_upload_size' => 25 * 1024 * 1024,
    'allowed_extensions' => [
        'pdf', 'txt', 'zip', 'rar', '7z', 'png', 'jpg', 'jpeg', 'gif', 'mp4', 'mp3', 'doc', 'docx', 'xls', 'xlsx',
    ],
    'admin' => [
        'username' => 'admin',
        'password_hash' => '$2y$12$T3LLNgxEC0njPEcGOURLqu2wTaX/1Dvw/pFDmeDcvoHszmnwKHcCO',
    ],
    'timezone' => 'UTC',
];
