import json
import sys
import time
import urllib.error
import urllib.request
import uuid

base = sys.argv[1] if len(sys.argv) > 1 else 'http://127.0.0.1:5080'
tokens = {}
def request(path, method='GET', data=None, role='Redactor', expected=200, content_type='application/json'):
    body = data if isinstance(data, bytes) else json.dumps(data).encode() if data is not None else None
    headers = {'Content-Type': content_type}
    if role in tokens:
        headers['Authorization'] = 'Bearer ' + tokens[role]
    try:
        response = urllib.request.urlopen(urllib.request.Request(base + path, data=body, headers=headers, method=method))
    except urllib.error.HTTPError as error:
        response = error
    payload = response.read()
    assert response.code == expected, (path, response.code, payload[:400])
    return json.loads(payload) if payload and 'json' in response.headers.get('Content-Type', '') else payload

for role in ['Redactor', 'Revisor', 'Aprobador', 'Lector']:
    tokens[role] = request('/api/auth/login', 'POST', {'email': role.lower() + '@techdocs.demo', 'password': 'Demo1234!'})['token']
system = request('/api/systems')[0]['id']
part = request('/api/parts')['items'][0]
code = 'QA-' + uuid.uuid4().hex[:8].upper()
document = {'code': code, 'title': 'Prueba completa de aprobación', 'systemId': system, 'blocks': [{'type': 'step', 'text': 'Inspeccionar junta tórica de demostración.'}], 'partIds': [part['id']]}
created = request('/api/documents', 'POST', document, expected=201)
identifier = created['id']
request('/api/documents', 'POST', document, role='Lector', expected=403)
request('/api/documents', 'POST', document, expected=409)
request(f'/api/documents/{identifier}/transition', 'POST', {'action': 'submit', 'expectedRevision': 1, 'reason': None}, expected=204)
request(f'/api/documents/{identifier}/transition', 'POST', {'action': 'approve', 'expectedRevision': 2, 'reason': None}, role='Aprobador', expected=409)
request(f'/api/documents/{identifier}/transition', 'POST', {'action': 'review', 'expectedRevision': 2, 'reason': None}, role='Revisor', expected=204)
request(f'/api/documents/{identifier}/transition', 'POST', {'action': 'approve', 'expectedRevision': 3, 'reason': None}, role='Aprobador', expected=204)
approved = request(f'/api/documents/{identifier}')
assert approved['state'] == 'Approved'
pdf = request('/api/manual.pdf?ids=' + identifier, role='Lector')
assert pdf.startswith(b'%PDF-')
document.update(title='Nueva edición de la prueba', expectedRevision=approved['revision'])
document['blocks'][0]['text'] = 'Contenido de la segunda versión.'
request(f'/api/documents/{identifier}', 'PUT', document)
assert request(f'/api/documents/{identifier}?version=1')['title'] == 'Prueba completa de aprobación'
assert request(f'/api/documents/{identifier}?version=1')['blocks'][0]['text'] != document['blocks'][0]['text']
updated = request('/api/parts/' + part['id'], 'PUT', {'name': part['name'], 'specification': 'Revisión de prueba', 'expectedRevision': part['revision']})
assert len(updated['affectedDocuments']) >= 1
assert request('/api/parts/' + part['id'])['revision'] == part['revision'] + 1
assert any(t['documentId'] == identifier for t in request('/api/tasks'))
for attempt in range(15):
    found = request('/api/documents?q=junta%20torica')
    if found['total'] > 0:
        break
    time.sleep(1)
assert found['total'] > 0
print(json.dumps({'result': 'passed', 'documentId': identifier, 'workflow': True, 'roles': True, 'versions': True, 'impact': True, 'cacheInvalidation': True, 'search': True, 'pdf': True}, indent=2))
