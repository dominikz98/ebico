'use strict';

/*
 * EBICO vendor-capture tool (issue #59).
 *
 * Drives the third-party OSS EBICS client `ebics-client` (github.com/node-ebics/node-ebics-client,
 * MIT) through its INI / HIA / HPB onboarding orders and captures the exact request XML it puts on the
 * wire, writing it into EBICO's conformance corpus. The captures are then replayed against the real
 * server by tests/EBICO.Tests/Conformance/VendorCaptureConformanceTests.cs.
 *
 * This runs ONCE, LOCALLY, OFFLINE — it is not part of `dotnet build`/`dotnet test` or CI. The client
 * posts to a throwaway local sink (never a real bank); we record the request body and discard the
 * (faked) response. All key material is generated fresh here and is disposable — see PROVENANCE.md.
 *
 * Usage:  cd tools/vendor-capture && npm install && node capture.js
 */

const http = require('http');
const fs = require('fs');
const path = require('path');
const { Client, Orders } = require('ebics-client');

// ebics-client speaks EBICS 2.x on the H004 wire (see its predefinedOrders/*.js: version 'h004').
const CLIENT_NAME = 'node-ebics-client';
const EBICS_VERSION = 'H004';

// Stable, EBICS-identifier-safe ids ([a-zA-Z0-9,=]) the replay test seeds a matching subscriber for.
const IDS = { hostId: 'EBICOHOST', partnerId: 'PARTNER1', userId: 'USER1' };

const outputDir = path.resolve(
    __dirname, '..', '..', 'tests', 'EBICO.Tests', 'Conformance', 'Vendor',
    CLIENT_NAME, EBICS_VERSION, 'request');

let capturedBody = null;
const sink = http.createServer((req, res) => {
    const chunks = [];
    req.on('data', (chunk) => chunks.push(chunk));
    req.on('end', () => {
        capturedBody = Buffer.concat(chunks).toString('utf-8');
        // A minimal 200 so the client's transport resolves; its response parser then rejects this stub,
        // which we swallow — the request bytes were already captured above.
        res.writeHead(200, { 'content-type': 'text/xml' });
        res.end('<ebicsKeyManagementResponse/>');
    });
});

// In-memory key storage: ebics-client writes an encrypted blob and reads it back verbatim.
let storedKeys = null;
const keyStorage = { read: async () => storedKeys, write: async (blob) => { storedKeys = blob; } };

async function main() {
    await new Promise((resolve) => sink.listen(0, resolve));
    const port = sink.address().port;

    const client = new Client({
        url: `http://localhost:${port}/ebics`,
        passphrase: 'ebico-vendor-capture-throwaway',
        keyStorage,
        ...IDS,
    });

    fs.mkdirSync(outputDir, { recursive: true });

    // INI generates and persists the throwaway key set, so HIA/HPB below reuse the same keys.
    for (const [order, fileName] of [[Orders.INI, 'ini.xml'], [Orders.HIA, 'hia.xml'], [Orders.HPB, 'hpb.xml']]) {
        capturedBody = null;
        try {
            await client.send(order);
        } catch {
            // The faked sink response is not a real EBICS response; the client throws parsing it. The
            // request we care about was already captured by the sink.
        }

        if (!capturedBody) {
            throw new Error(`No request captured for ${fileName}.`);
        }

        const target = path.join(outputDir, fileName);
        fs.writeFileSync(target, capturedBody, 'utf-8');
        console.log(`wrote ${path.relative(path.resolve(__dirname, '..', '..'), target)} (${capturedBody.length} bytes)`);
    }

    sink.close();
}

main().catch((error) => {
    console.error('vendor capture failed:', error);
    process.exit(1);
});
