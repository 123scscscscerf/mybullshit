<?php

declare(strict_types=1);

require_once __DIR__ . '/../includes/db.php';

$pdo = db();
$config = appConfig();
$fileId = isset($_GET['file']) ? sanitizeFileId((string) $_GET['file']) : '';

if ($fileId !== '') {
    $file = fetchFileById($pdo, $fileId);
    if ($file === null) {
        http_response_code(404);
        ?>
        <!doctype html>
        <html lang="en">
        <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <title>File not found</title>
            <link rel="stylesheet" href="/style.css">
        </head>
        <body>
        <main class="container center">
            <section class="card">
                <h1>404 · File ID not found</h1>
                <p class="meta">The file ID <span class="badge"><?= htmlspecialchars($fileId, ENT_QUOTES, 'UTF-8') ?></span> does not exist.</p>
                <p><a class="btn secondary" href="/index.php">Back to home</a></p>
            </section>
        </main>
        </body>
        </html>
        <?php
        exit;
    }

    ?>
    <!doctype html>
    <html lang="en">
    <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width,initial-scale=1">
        <title><?= htmlspecialchars($file['id'], ENT_QUOTES, 'UTF-8') ?> · <?= htmlspecialchars($config['app_name'], ENT_QUOTES, 'UTF-8') ?></title>
        <link rel="stylesheet" href="/style.css">
    </head>
    <body>
    <main class="container">
        <header class="header">
            <h1>File details</h1>
            <nav>
                <a class="btn secondary" href="/index.php">Home</a>
                <a class="btn secondary" href="/about.php">System</a>
            </nav>
        </header>

        <section class="card">
            <p><strong>File ID:</strong> <span class="badge"><?= htmlspecialchars($file['id'], ENT_QUOTES, 'UTF-8') ?></span></p>
            <p><strong>Original name:</strong> <?= htmlspecialchars($file['original_name'], ENT_QUOTES, 'UTF-8') ?></p>
            <p><strong>Uploaded at:</strong> <?= htmlspecialchars($file['uploaded_at'], ENT_QUOTES, 'UTF-8') ?></p>
            <p><strong>File size:</strong> <?= htmlspecialchars(formatBytes((int) $file['size']), ENT_QUOTES, 'UTF-8') ?></p>
            <p><a class="btn" href="/download.php?id=<?= urlencode($file['id']) ?>">Download</a></p>
        </section>
    </main>
    </body>
    </html>
    <?php
    exit;
}

$files = fetchAllFiles($pdo);
?>
<!doctype html>
<html lang="en">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width,initial-scale=1">
    <title><?= htmlspecialchars($config['app_name'], ENT_QUOTES, 'UTF-8') ?></title>
    <link rel="stylesheet" href="/style.css">
</head>
<body>
<main class="container">
    <header class="header">
        <div>
            <h1><?= htmlspecialchars($config['app_name'], ENT_QUOTES, 'UTF-8') ?></h1>
            <p class="meta">Secure file index and download gateway.</p>
        </div>
        <nav>
            <a class="btn secondary" href="/about.php">System</a>
            <a class="btn" href="/admin/index.php">Admin</a>
        </nav>
    </header>

    <section class="card table-wrap">
        <h2>Available files</h2>
        <?php if ($files === []): ?>
            <p class="meta">No files uploaded yet.</p>
        <?php else: ?>
            <table>
                <thead>
                <tr>
                    <th>File ID</th>
                    <th>Original file name</th>
                    <th>Upload date</th>
                    <th>File size</th>
                </tr>
                </thead>
                <tbody>
                <?php foreach ($files as $file): ?>
                    <tr>
                        <td><a href="/index.php?file=<?= urlencode($file['id']) ?>"><span class="badge"><?= htmlspecialchars($file['id'], ENT_QUOTES, 'UTF-8') ?></span></a></td>
                        <td><?= htmlspecialchars($file['original_name'], ENT_QUOTES, 'UTF-8') ?></td>
                        <td><?= htmlspecialchars($file['uploaded_at'], ENT_QUOTES, 'UTF-8') ?></td>
                        <td><?= htmlspecialchars(formatBytes((int) $file['size']), ENT_QUOTES, 'UTF-8') ?></td>
                    </tr>
                <?php endforeach; ?>
                </tbody>
            </table>
        <?php endif; ?>
    </section>
</main>
</body>
</html>
