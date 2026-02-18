<?php

declare(strict_types=1);

require_once __DIR__ . '/../includes/db.php';

$pdo = db();
$config = appConfig();
$totalFiles = count(fetchAllFiles($pdo));
$storage = totalStorageUsed($pdo);
?>
<!doctype html>
<html lang="en">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width,initial-scale=1">
    <title>System · <?= htmlspecialchars($config['app_name'], ENT_QUOTES, 'UTF-8') ?></title>
    <link rel="stylesheet" href="/style.css">
</head>
<body>
<main class="container">
    <header class="header">
        <h1>System status</h1>
        <a class="btn secondary" href="/index.php">Home</a>
    </header>

    <section class="grid">
        <article class="card">
            <h3>PHP version</h3>
            <p><?= htmlspecialchars(PHP_VERSION, ENT_QUOTES, 'UTF-8') ?></p>
        </article>
        <article class="card">
            <h3>Server time</h3>
            <p><?= htmlspecialchars(date('Y-m-d H:i:s T'), ENT_QUOTES, 'UTF-8') ?></p>
        </article>
        <article class="card">
            <h3>Total files</h3>
            <p><?= htmlspecialchars((string) $totalFiles, ENT_QUOTES, 'UTF-8') ?></p>
        </article>
        <article class="card">
            <h3>Total storage used</h3>
            <p><?= htmlspecialchars(formatBytes($storage), ENT_QUOTES, 'UTF-8') ?></p>
        </article>
    </section>
</main>
</body>
</html>
