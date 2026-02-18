<?php

declare(strict_types=1);

function appConfig(): array
{
    static $config = null;

    if ($config === null) {
        $config = require __DIR__ . '/config.php';
        date_default_timezone_set($config['timezone'] ?? 'UTC');
    }

    return $config;
}

function db(): PDO
{
    static $pdo = null;

    if ($pdo instanceof PDO) {
        return $pdo;
    }

    $config = appConfig();
    $dbPath = $config['db_path'];
    $dbDir = dirname($dbPath);

    if (!is_dir($dbDir)) {
        mkdir($dbDir, 0755, true);
    }

    $pdo = new PDO('sqlite:' . $dbPath);
    $pdo->setAttribute(PDO::ATTR_ERRMODE, PDO::ERRMODE_EXCEPTION);
    $pdo->setAttribute(PDO::ATTR_DEFAULT_FETCH_MODE, PDO::FETCH_ASSOC);

    initializeDatabase($pdo);

    return $pdo;
}

function initializeDatabase(PDO $pdo): void
{
    $pdo->exec(
        'CREATE TABLE IF NOT EXISTS files (
            id VARCHAR(64) PRIMARY KEY,
            original_name TEXT NOT NULL,
            stored_name TEXT NOT NULL,
            size INTEGER NOT NULL,
            uploaded_at DATETIME NOT NULL
        )'
    );
}

function formatBytes(int $bytes): string
{
    if ($bytes < 1024) {
        return $bytes . ' B';
    }

    $units = ['KB', 'MB', 'GB', 'TB'];
    $size = $bytes / 1024;
    $unitIndex = 0;

    while ($size >= 1024 && $unitIndex < count($units) - 1) {
        $size /= 1024;
        $unitIndex++;
    }

    return number_format($size, 2) . ' ' . $units[$unitIndex];
}

function sanitizeFileId(string $id): string
{
    return preg_replace('/[^a-zA-Z0-9_-]/', '', trim($id)) ?? '';
}

function generateFileId(PDO $pdo): string
{
    do {
        $id = 'id' . random_int(100000000, 999999999);
        $stmt = $pdo->prepare('SELECT COUNT(*) FROM files WHERE id = :id');
        $stmt->execute(['id' => $id]);
    } while ((int) $stmt->fetchColumn() > 0);

    return $id;
}

function fetchFileById(PDO $pdo, string $id): ?array
{
    $stmt = $pdo->prepare('SELECT * FROM files WHERE id = :id LIMIT 1');
    $stmt->execute(['id' => $id]);
    $file = $stmt->fetch();

    return $file ?: null;
}

function fetchAllFiles(PDO $pdo): array
{
    $stmt = $pdo->query('SELECT * FROM files ORDER BY uploaded_at DESC');

    return $stmt->fetchAll();
}

function totalStorageUsed(PDO $pdo): int
{
    $stmt = $pdo->query('SELECT COALESCE(SUM(size), 0) FROM files');

    return (int) $stmt->fetchColumn();
}
