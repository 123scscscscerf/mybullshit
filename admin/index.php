<?php

declare(strict_types=1);

require_once __DIR__ . '/../includes/auth.php';

$pdo = db();
$config = appConfig();
startSecureSession();

$errors = [];
$success = null;

if (isset($_GET['logout'])) {
    logoutAdmin();
    header('Location: /admin/index.php');
    exit;
}

if (!isAdminLoggedIn() && $_SERVER['REQUEST_METHOD'] === 'POST' && ($_POST['action'] ?? '') === 'login') {
    $username = trim((string) ($_POST['username'] ?? ''));
    $password = (string) ($_POST['password'] ?? '');

    if (!attemptLogin($username, $password)) {
        $errors[] = 'Invalid username or password.';
    } else {
        header('Location: /admin/index.php');
        exit;
    }
}

if (isAdminLoggedIn() && $_SERVER['REQUEST_METHOD'] === 'POST' && ($_POST['action'] ?? '') === 'upload') {
    if (!isset($_FILES['upload']) || $_FILES['upload']['error'] !== UPLOAD_ERR_OK) {
        $errors[] = 'Please choose a file to upload.';
    } else {
        $file = $_FILES['upload'];
        $manualId = sanitizeFileId((string) ($_POST['file_id'] ?? ''));
        $uploadSize = (int) $file['size'];

        if ($uploadSize <= 0) {
            $errors[] = 'Uploaded file is empty.';
        }

        if ($uploadSize > $config['max_upload_size']) {
            $errors[] = 'File exceeds max upload size of ' . formatBytes($config['max_upload_size']) . '.';
        }

        $originalName = (string) $file['name'];
        $extension = strtolower(pathinfo($originalName, PATHINFO_EXTENSION));
        if (!in_array($extension, $config['allowed_extensions'], true)) {
            $errors[] = 'This file extension is not allowed.';
        }

        $targetId = $manualId !== '' ? $manualId : generateFileId($pdo);

        if ($manualId !== '' && fetchFileById($pdo, $targetId) !== null) {
            $errors[] = 'The provided file ID already exists.';
        }

        if ($errors === []) {
            $storedName = bin2hex(random_bytes(16)) . ($extension !== '' ? '.' . $extension : '');
            $destination = __DIR__ . '/../files/' . $storedName;

            if (!move_uploaded_file($file['tmp_name'], $destination)) {
                $errors[] = 'Could not move uploaded file.';
            } else {
                $stmt = $pdo->prepare('INSERT INTO files (id, original_name, stored_name, size, uploaded_at) VALUES (:id, :original_name, :stored_name, :size, :uploaded_at)');
                $stmt->execute([
                    'id' => $targetId,
                    'original_name' => $originalName,
                    'stored_name' => $storedName,
                    'size' => $uploadSize,
                    'uploaded_at' => date('Y-m-d H:i:s'),
                ]);
                $success = 'File uploaded successfully with ID ' . $targetId . '.';
            }
        }
    }
}

if (isAdminLoggedIn() && $_SERVER['REQUEST_METHOD'] === 'POST' && ($_POST['action'] ?? '') === 'delete') {
    $deleteId = sanitizeFileId((string) ($_POST['id'] ?? ''));
    $file = fetchFileById($pdo, $deleteId);

    if ($file === null) {
        $errors[] = 'File not found for deletion.';
    } else {
        $path = __DIR__ . '/../files/' . basename($file['stored_name']);
        if (is_file($path)) {
            unlink($path);
        }

        $stmt = $pdo->prepare('DELETE FROM files WHERE id = :id');
        $stmt->execute(['id' => $deleteId]);
        $success = 'File deleted successfully.';
    }
}

$files = isAdminLoggedIn() ? fetchAllFiles($pdo) : [];
?>
<!doctype html>
<html lang="en">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width,initial-scale=1">
    <title>Admin · <?= htmlspecialchars($config['app_name'], ENT_QUOTES, 'UTF-8') ?></title>
    <link rel="stylesheet" href="/style.css">
</head>
<body>
<main class="container">
    <header class="header">
        <h1>Admin panel</h1>
        <nav>
            <a class="btn secondary" href="/index.php">Home</a>
            <?php if (isAdminLoggedIn()): ?>
                <a class="btn danger" href="/admin/index.php?logout=1">Logout</a>
            <?php endif; ?>
        </nav>
    </header>

    <?php foreach ($errors as $error): ?>
        <div class="alert error"><?= htmlspecialchars($error, ENT_QUOTES, 'UTF-8') ?></div>
    <?php endforeach; ?>

    <?php if ($success !== null): ?>
        <div class="alert success"><?= htmlspecialchars($success, ENT_QUOTES, 'UTF-8') ?></div>
    <?php endif; ?>

    <?php if (!isAdminLoggedIn()): ?>
        <section class="card" style="max-width:420px; margin:0 auto;">
            <h2>Login</h2>
            <form method="post">
                <input type="hidden" name="action" value="login">
                <div class="input-group">
                    <label for="username">Username</label>
                    <input id="username" type="text" name="username" required>
                </div>
                <div class="input-group">
                    <label for="password">Password</label>
                    <input id="password" type="password" name="password" required>
                </div>
                <button class="btn" type="submit">Sign in</button>
            </form>
        </section>
    <?php else: ?>
        <section class="card">
            <h2>Upload new file</h2>
            <p class="meta">Allowed extensions: <?= htmlspecialchars(implode(', ', $config['allowed_extensions']), ENT_QUOTES, 'UTF-8') ?> · Max: <?= htmlspecialchars(formatBytes($config['max_upload_size']), ENT_QUOTES, 'UTF-8') ?></p>
            <form method="post" enctype="multipart/form-data">
                <input type="hidden" name="action" value="upload">
                <div class="input-group">
                    <label for="upload">File</label>
                    <input id="upload" type="file" name="upload" required>
                </div>
                <div class="input-group">
                    <label for="file_id">Manual File ID (optional)</label>
                    <input id="file_id" type="text" name="file_id" placeholder="id128370912">
                </div>
                <button class="btn" type="submit">Upload file</button>
            </form>
        </section>

        <section class="card table-wrap" style="margin-top:1rem;">
            <h2>All files</h2>
            <?php if ($files === []): ?>
                <p class="meta">No files in database.</p>
            <?php else: ?>
                <table>
                    <thead>
                    <tr>
                        <th>ID</th>
                        <th>Name</th>
                        <th>Size</th>
                        <th>Uploaded</th>
                        <th>Action</th>
                    </tr>
                    </thead>
                    <tbody>
                    <?php foreach ($files as $file): ?>
                        <tr>
                            <td><span class="badge"><?= htmlspecialchars($file['id'], ENT_QUOTES, 'UTF-8') ?></span></td>
                            <td><?= htmlspecialchars($file['original_name'], ENT_QUOTES, 'UTF-8') ?></td>
                            <td><?= htmlspecialchars(formatBytes((int) $file['size']), ENT_QUOTES, 'UTF-8') ?></td>
                            <td><?= htmlspecialchars($file['uploaded_at'], ENT_QUOTES, 'UTF-8') ?></td>
                            <td>
                                <form method="post" onsubmit="return confirm('Delete this file?');">
                                    <input type="hidden" name="action" value="delete">
                                    <input type="hidden" name="id" value="<?= htmlspecialchars($file['id'], ENT_QUOTES, 'UTF-8') ?>">
                                    <button class="btn danger" type="submit">Delete</button>
                                </form>
                            </td>
                        </tr>
                    <?php endforeach; ?>
                    </tbody>
                </table>
            <?php endif; ?>
        </section>
    <?php endif; ?>
</main>
</body>
</html>
