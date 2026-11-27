// The HTTPS endpoint RestEndpointObservationTests calls (README.md beside this file says how to run it). On 443 the path picks a
// reply, as a service would send it; on 8443 the path picks the exact bytes of an HTTP/1.0 reply, for what a service would rarely
// send: no headers, an empty reason phrase, a header twice, a scalar body.
import https from 'node:https';
import tls from 'node:tls';
import fs from 'node:fs';

const certificate = { key: fs.readFileSync('/certs/endpoint.key'), cert: fs.readFileSync('/certs/endpoint.crt') };

https.createServer(certificate, (request, response) => {
  let body = '';
  request.on('data', chunk => body += chunk);
  request.on('end', () => {
    const url = new URL(request.url, 'https://endpoint');
    const path = url.pathname;
    const json = (status, value) => {
      response.writeHead(status, { 'content-type': 'application/json' });
      response.end(JSON.stringify(value));
    };
    const text = (status, type, value) => {
      response.writeHead(status, type ? { 'content-type': type } : {});
      response.end(value);
    };
    if (path.startsWith('/status/'))
      return json(Number(path.slice('/status/'.length)), { error: path });
    switch (path) {
      case '/text': return text(200, 'text/plain', 'plain words');
      case '/xml': return text(200, 'application/xml', '<a><b>1</b></a>');
      case '/empty': return text(204, null, '');
      case '/empty200': return text(200, 'application/json', '');
      case '/broken': return text(200, 'application/json', '{not json');
      case '/array': return json(200, [1, 2]);
      case '/null': return text(200, 'application/json', 'null');
      case '/number': return text(200, 'application/json', '42');
      case '/slow': return setTimeout(() => json(200, { slow: true }), 10000);
      case '/echo': return json(200, { method: request.method, target: request.url, body });
      default: return json(200, { name: 'plan', target: request.url });
    }
  });
}).listen(443);

const raw = {
  '/bare': 'HTTP/1.0 200 OK\r\n\r\n{"a":1}',
  '/no-reason': 'HTTP/1.0 200 \r\n\r\n{"a":1}',
  '/twice': 'HTTP/1.0 599 Strange\r\nX-Count: 5\r\nX-Count: 6\r\n\r\n{"a":1}',
  '/string': 'HTTP/1.0 200 OK\r\nContent-Type: application/json\r\n\r\n"words"',
  '/true': 'HTTP/1.0 200 OK\r\nContent-Type: application/json\r\n\r\ntrue',
};
tls.createServer(certificate, socket => {
  socket.once('data', request => {
    const path = request.toString().split(' ')[1];
    socket.end(raw[path] ?? 'HTTP/1.0 404 Not Found\r\n\r\n');
  });
}).listen(8443);
