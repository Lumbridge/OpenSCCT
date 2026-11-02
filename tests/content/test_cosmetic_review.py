"""A new rendering pass cannot certify old or externally changed baked pixels."""
import importlib.util
from pathlib import Path

import pytest


@pytest.fixture
def preview():
    path=Path(__file__).resolve().parents[2]/'tools/assets/preview_fitted_skins.py'
    spec=importlib.util.spec_from_file_location('cosmetic_review_test',path)
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    return module


@pytest.mark.parametrize('changed_file',['colour','mask'])
def test_review_requires_bake_time_inputs_and_unmodified_outputs(tmp_path,monkeypatch,preview,changed_file):
    output=tmp_path/'cosmetic_woodland_test.png';output.write_bytes(b'original colour pixels')
    mask=output.with_suffix('.mask.png');mask.write_bytes(b'original mask pixels')
    job=('source.png',str(output),'woodland','test',(16,16),'model.glb')
    monkeypatch.setattr(preview,'job_signature',lambda job,sources:sources['stamp'])
    with pytest.raises(ValueError,match='Missing bake record'):
        preview.validate_records(tmp_path,[job],{}, {'stamp':'original inputs'})
    records={}
    preview.record_bake(tmp_path,job,'original inputs',records)
    preview.save_records(tmp_path,records)
    records=preview.load_records(tmp_path)
    preview.validate_records(tmp_path,[job],records,{'stamp':'original inputs'})
    with pytest.raises(ValueError,match='Inputs changed since bake'):
        preview.validate_records(tmp_path,[job],records,{'stamp':'different source artwork'})
    (output if changed_file=='colour' else mask).write_bytes(b'replaced pixels')
    with pytest.raises(ValueError,match='Output changed since bake'):
        preview.validate_records(tmp_path,[job],records,{'stamp':'original inputs'})
